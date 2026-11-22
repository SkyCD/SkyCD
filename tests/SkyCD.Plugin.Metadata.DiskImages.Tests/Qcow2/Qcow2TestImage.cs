using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Streams;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>
/// Builds small QCOW2 version 3 images around a FAT volume so tests can exercise the reader without
/// shipping large binary fixtures or depending on qemu-img.
/// </summary>
internal static class Qcow2TestImage
{
    public const int ClusterSize = 4096;
    private const int ClusterBits = 12;
    private const int L2Entries = ClusterSize / 8;
    private const int MetadataClusters = 4;
    private const int CompressedOffsetBits = 70 - ClusterBits;
    private const int BackingNameOffset = 112;

    private static readonly byte[] QcowMagic = [0x51, 0x46, 0x49, 0xfb];

    public static byte[] CreateFatDisk(params (string Path, string Content)[] files) =>
        AddPartition(CreateFatVolume(files));

    /// <summary>Creates a file system image without a partition table, as in floppy-style images.</summary>
    public static byte[] CreateFatVolume(params (string Path, string Content)[] files)
    {
        using var volume = new MemoryStream();
        FatFileSystem.FormatFloppy(volume, FloppyDiskType.HighDensity, "DATA");
        volume.Position = 0;
        using (var fileSystem = new FatFileSystem(volume, Ownership.None))
        {
            foreach (var (path, content) in files)
            {
                var rawPath = path.Replace('/', '\\');
                var parent = Path.GetDirectoryName(path.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(parent))
                {
                    fileSystem.CreateDirectory(parent.Replace('/', '\\'));
                }

                using var writer = new BinaryWriter(fileSystem.OpenFile(rawPath, FileMode.Create, FileAccess.Write));
                writer.Write(System.Text.Encoding.UTF8.GetBytes(content));
            }
        }

        return volume.ToArray();
    }

    /// <summary>Wraps a file system image in an MBR so DiscUtils reports it as a physical volume.</summary>
    private static byte[] AddPartition(byte[] volume)
    {
        const int startSector = 63;
        const int sectorSize = 512;
        var disk = new byte[(startSector * sectorSize) + volume.Length];
        var partition = disk.AsSpan(446, 16);
        partition[4] = 0x0E;
        BinaryPrimitives.WriteUInt32LittleEndian(partition[8..], startSector);
        BinaryPrimitives.WriteUInt32LittleEndian(partition[12..], (uint)(volume.Length / sectorSize));
        disk[510] = 0x55;
        disk[511] = 0xAA;
        volume.CopyTo(disk, startSector * sectorSize);
        return disk;
    }

    public static byte[] Build(
        byte[] diskContent,
        bool compress = false,
        string? backingFileName = null,
        uint cryptMethod = 0,
        ulong incompatibleFeatures = 0,
        uint compressionType = 0)
    {
        ArgumentNullException.ThrowIfNull(diskContent);

        var guestClusters = Math.Max(1, (diskContent.Length + ClusterSize - 1) / ClusterSize);
        var l1Entries = (guestClusters + L2Entries - 1) / L2Entries;
        if (l1Entries > L2Entries)
        {
            throw new InvalidOperationException("Test images must fit in one L2 table.");
        }

        var backingName = string.IsNullOrEmpty(backingFileName)
            ? []
            : System.Text.Encoding.UTF8.GetBytes(backingFileName);
        if (BackingNameOffset + backingName.Length > ClusterSize)
        {
            throw new InvalidOperationException("Backing file name does not fit in the first cluster.");
        }

        var dataClusters = compress ? CompressClusters(diskContent, guestClusters) : RawClusters(diskContent, guestClusters);
        var image = new byte[(MetadataClusters + dataClusters.Count) * ClusterSize];

        QcowMagic.CopyTo(image, 0);
        WriteUInt32(image, 4, 3);
        WriteUInt64(image, 8, backingName.Length == 0 ? 0UL : (ulong)BackingNameOffset);
        WriteUInt32(image, 16, (uint)backingName.Length);
        WriteUInt32(image, 20, ClusterBits);
        WriteUInt64(image, 24, (ulong)diskContent.Length);
        WriteUInt32(image, 32, cryptMethod);
        WriteUInt32(image, 36, (uint)l1Entries);
        WriteUInt64(image, 40, ClusterSize);
        WriteUInt64(image, 48, 3L * ClusterSize);
        WriteUInt32(image, 56, 1);
        WriteUInt32(image, 60, 0);
        WriteUInt64(image, 64, 0);
        WriteUInt64(image, 72, incompatibleFeatures);
        WriteUInt64(image, 80, 0);
        WriteUInt64(image, 88, 0);
        WriteUInt32(image, 96, 4);
        WriteUInt32(image, 100, compressionType == 0 ? 104u : 112u);
        if (compressionType != 0)
        {
            WriteUInt32(image, 104, compressionType);
        }

        backingName.CopyTo(image, BackingNameOffset);

        WriteUInt64(image, ClusterSize, 2L * ClusterSize);

        var l2Base = 2 * ClusterSize;
        var hostCluster = MetadataClusters;
        foreach (var cluster in dataClusters)
        {
            var hostOffset = (long)hostCluster * ClusterSize;
            cluster.Payload.CopyTo(image, hostOffset);

            var entry = (ulong)hostOffset;
            if (compress)
            {
                var sectors = (cluster.Payload.Length + 511) / 512;
                entry |= ((ulong)(sectors - 1) << CompressedOffsetBits) | (1UL << 62);
            }

            WriteUInt64(image, l2Base + (cluster.GuestIndex * 8), entry);
            hostCluster++;
        }

        return image;
    }

    public static byte[] BuildOverlay(int virtualSize, string backingFileName)
    {
        var zeros = new byte[virtualSize];
        return Build(zeros, backingFileName: backingFileName);
    }

    private static List<ClusterData> RawClusters(byte[] diskContent, int guestClusters)
    {
        var clusters = new List<ClusterData>();
        for (var index = 0; index < guestClusters; index++)
        {
            var slice = Slice(diskContent, index);
            if (slice.Any(value => value != 0))
            {
                clusters.Add(new ClusterData(index, slice));
            }
        }

        return clusters;
    }

    private static List<ClusterData> CompressClusters(byte[] diskContent, int guestClusters)
    {
        var clusters = new List<ClusterData>();
        for (var index = 0; index < guestClusters; index++)
        {
            var slice = Slice(diskContent, index);
            if (!slice.Any(value => value != 0))
            {
                continue;
            }

            var compressed = Compress(slice);
            if (compressed.Length > ClusterSize)
            {
                throw new InvalidOperationException("Compressed cluster does not fit in one host cluster.");
            }

            clusters.Add(new ClusterData(index, compressed));
        }

        return clusters;
    }

    private static byte[] Slice(byte[] diskContent, int guestIndex)
    {
        var slice = new byte[ClusterSize];
        var start = guestIndex * ClusterSize;
        var count = Math.Min(ClusterSize, diskContent.Length - start);
        Array.Copy(diskContent, start, slice, 0, count);
        return slice;
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static void WriteUInt32(byte[] target, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(offset), value);

    private static void WriteUInt64(byte[] target, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64BigEndian(target.AsSpan(offset), value);

    private sealed record ClusterData(int GuestIndex, byte[] Payload);
}
