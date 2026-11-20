using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Streams;

namespace SkyCD.Plugin.Dmg.Tests;

/// <summary>
/// Builds small UDIF images around a FAT volume so tests can exercise the reader without shipping a
/// large binary fixture or depending on hdiutil. Every structure is written big-endian exactly as the
/// on-disk format requires.
/// </summary>
internal static class DmgTestImage
{
    public const uint RunTypeRaw = 1;
    public const uint RunTypeZeros = 2;
    public const uint RunTypeZlib = 0x8000_0005;
    public const uint RunTypeLzfse = 0x8000_0007;

    private const int SectorSize = 512;
    private const int TrailerSize = 512;
    private const int MishHeaderSize = 204;
    private const int MishRunSize = 40;

    private static ReadOnlySpan<byte> KolySignature => [0x6B, 0x6F, 0x6C, 0x79];
    private static ReadOnlySpan<byte> EncryptedSignature => "encrcdsa"u8;

    public static byte[] CreateFatDisk(params (string Path, string Content)[] files) =>
        AddPartition(CreateFatVolume(files));

    /// <summary>Creates a file system image without a partition table, as in floppy-style images.</summary>
    public static byte[] CreateFatVolume(params (string Path, string Content)[] files)
    {
        using var volume = new MemoryStream();
        FatFileSystem.FormatFloppy(volume, FloppyDiskType.HighDensity, "DMGDATA");
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
                writer.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return volume.ToArray();
    }

    /// <summary>
    /// Wraps a FAT volume in a single UDIF data fork followed by the XML property list and the
    /// <c>koly</c> trailer.
    /// </summary>
    public static byte[] Build(
        byte[] diskContent,
        bool compress = false,
        uint? runType = null,
        long trailingZeroSectors = 0,
        uint segmentNumber = 1,
        uint segmentCount = 1,
        bool corruptSignature = false,
        bool omitPropertyList = false,
        bool encrypted = false)
    {
        ArgumentNullException.ThrowIfNull(diskContent);
        if (diskContent.Length % SectorSize != 0)
        {
            throw new ArgumentException("Test disk content must be a whole number of sectors.", nameof(diskContent));
        }

        var payload = compress ? Compress(diskContent) : diskContent;
        var sectors = diskContent.Length / SectorSize;
        var runs = new List<Run>
        {
            new(runType ?? (compress ? RunTypeZlib : RunTypeRaw), 0, sectors, 0, payload.Length)
        };
        if (trailingZeroSectors > 0)
        {
            runs.Add(new Run(RunTypeZeros, sectors, trailingZeroSectors, 0, 0));
        }

        var totalSectors = sectors + trailingZeroSectors;
        var mish = BuildMish(totalSectors, runs);
        var xml = omitPropertyList ? [] : Encoding.UTF8.GetBytes(PropertyList(mish));
        var prefix = encrypted ? EncryptedSignature.ToArray() : [];

        using var image = new MemoryStream();
        image.Write(prefix);
        image.Write(payload);
        image.Write(xml);

        var payloadOffset = (ulong)prefix.Length;
        var xmlOffset = (ulong)(prefix.Length + payload.Length);
        var trailer = BuildTrailer(new TrailerOptions
        {
            DataForkOffset = payloadOffset,
            DataForkLength = (ulong)payload.Length,
            XmlOffset = xmlOffset,
            XmlLength = (ulong)xml.Length,
            SectorCount = totalSectors,
            SegmentNumber = segmentNumber,
            SegmentCount = segmentCount,
            CorruptSignature = corruptSignature
        });
        image.Write(trailer);

        return image.ToArray();
    }

    private static byte[] AddPartition(byte[] volume)
    {
        const int startSector = 63;
        var disk = new byte[(startSector * SectorSize) + volume.Length];
        var partition = disk.AsSpan(446, 16);
        partition[4] = 0x0E;
        BinaryPrimitives.WriteUInt32LittleEndian(partition[8..], startSector);
        BinaryPrimitives.WriteUInt32LittleEndian(partition[12..], (uint)(volume.Length / SectorSize));
        disk[510] = 0x55;
        disk[511] = 0xAA;
        volume.CopyTo(disk, startSector * SectorSize);
        return disk;
    }

    private static byte[] BuildMish(long totalSectors, IReadOnlyList<Run> runs)
    {
        var mish = new byte[MishHeaderSize + (runs.Count * MishRunSize)];
        var span = mish.AsSpan();

        Encoding.ASCII.GetBytes("mish").CopyTo(span);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 1);
        BinaryPrimitives.WriteUInt64BigEndian(span[8..], 0);
        BinaryPrimitives.WriteUInt64BigEndian(span[16..], (ulong)totalSectors);
        BinaryPrimitives.WriteUInt64BigEndian(span[24..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(span[32..], (uint)totalSectors);
        BinaryPrimitives.WriteUInt32BigEndian(span[36..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(span[200..], (uint)runs.Count);

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var offset = MishHeaderSize + (index * MishRunSize);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], run.Type);
            BinaryPrimitives.WriteUInt32BigEndian(span[(offset + 4)..], 0);
            BinaryPrimitives.WriteUInt64BigEndian(span[(offset + 8)..], (ulong)run.SectorStart);
            BinaryPrimitives.WriteUInt64BigEndian(span[(offset + 16)..], (ulong)run.SectorCount);
            BinaryPrimitives.WriteUInt64BigEndian(span[(offset + 24)..], (ulong)run.CompOffset);
            BinaryPrimitives.WriteUInt64BigEndian(span[(offset + 32)..], (ulong)run.CompLength);
        }

        return mish;
    }

    private static string PropertyList(byte[] mish) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0"><dict><key>resource-fork</key><dict><key>blkx</key><array><dict>
        <key>Attributes</key><string>0x0050</string>
        <key>CFName</key><string>whole disk (Apple_FAT32 : 0)</string>
        <key>Data</key><data>{Convert.ToBase64String(mish)}</data>
        <key>ID</key><string>0</string>
        <key>Name</key><string>whole disk (Apple_FAT32 : 0)</string>
        </dict></array></dict></dict></plist>
        """;

    private static byte[] BuildTrailer(TrailerOptions options)
    {
        var trailer = new byte[TrailerSize];
        var span = trailer.AsSpan();

        if (!options.CorruptSignature)
        {
            KolySignature.CopyTo(span);
        }

        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 4);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], TrailerSize);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 1);
        BinaryPrimitives.WriteUInt64BigEndian(span[16..], 0);
        BinaryPrimitives.WriteUInt64BigEndian(span[24..], options.DataForkOffset);
        BinaryPrimitives.WriteUInt64BigEndian(span[32..], options.DataForkLength);
        BinaryPrimitives.WriteUInt32BigEndian(span[56..], options.SegmentNumber);
        BinaryPrimitives.WriteUInt32BigEndian(span[60..], options.SegmentCount);
        BinaryPrimitives.WriteUInt64BigEndian(span[216..], options.XmlOffset);
        BinaryPrimitives.WriteUInt64BigEndian(span[224..], options.XmlLength);
        BinaryPrimitives.WriteUInt32BigEndian(span[488..], 1);
        BinaryPrimitives.WriteUInt64BigEndian(span[492..], (ulong)options.SectorCount);

        return trailer;
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private sealed record Run(uint Type, long SectorStart, long SectorCount, long CompOffset, long CompLength);

    private sealed class TrailerOptions
    {
        public ulong DataForkOffset { get; init; }

        public ulong DataForkLength { get; init; }

        public ulong XmlOffset { get; init; }

        public ulong XmlLength { get; init; }

        public long SectorCount { get; init; }

        public uint SegmentNumber { get; init; }

        public uint SegmentCount { get; init; }

        public bool CorruptSignature { get; init; }
    }
}
