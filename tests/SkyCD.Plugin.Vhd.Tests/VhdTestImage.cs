using System;
using System.IO;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Streams;
using VhdDisk = DiscUtils.Vhd.Disk;
using VhdxDisk = DiscUtils.Vhdx.Disk;

namespace SkyCD.Plugin.Vhd.Tests;

/// <summary>
/// Builds small VHD and VHDX images around a FAT volume so tests can exercise the reader without
/// shipping large binary fixtures or depending on Hyper-V tooling.
/// </summary>
internal static class VhdTestImage
{
    private const int SectorSize = 512;

    public static byte[] CreateFatVolume(params (string Path, string Content)[] files)
    {
        using var volume = new MemoryStream();
        FatFileSystem.FormatFloppy(volume, FloppyDiskType.DoubleDensity, "DATA");
        volume.Position = 0;
        using (var fileSystem = new FatFileSystem(volume, Ownership.None))
        {
            foreach (var (path, content) in files)
            {
                var rawPath = path.Replace('/', '\\');
                var parent = System.IO.Path.GetDirectoryName(path.Replace('\\', '/'));
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

    public static byte[] BuildVhd(byte[] diskContent)
    {
        ArgumentNullException.ThrowIfNull(diskContent);
        using var stream = new MemoryStream();
        VhdDisk.InitializeDynamic(stream, Ownership.None, Capacity(diskContent));
        stream.Position = 0;
        using (var disk = new VhdDisk(stream, Ownership.None))
        {
            WriteContent(disk, diskContent);
        }

        return stream.ToArray();
    }

    public static byte[] BuildVhdx(byte[] diskContent)
    {
        ArgumentNullException.ThrowIfNull(diskContent);
        using var stream = new MemoryStream();
        VhdxDisk.InitializeDynamic(stream, Ownership.None, Capacity(diskContent));
        stream.Position = 0;
        using (var disk = new VhdxDisk(stream, Ownership.None))
        {
            WriteContent(disk, diskContent);
        }

        return stream.ToArray();
    }

    /// <summary>Writes a dynamic VHD whose content comes from the supplied FAT volume.</summary>
    public static void WriteVhdFile(string path, byte[] diskContent)
    {
        var bytes = BuildVhd(diskContent);
        File.WriteAllBytes(path, bytes);
    }

    public static void WriteVhdxFile(string path, byte[] diskContent)
    {
        var bytes = BuildVhdx(diskContent);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Creates a VHD differencing disk that references the supplied parent image.</summary>
    public static void CreateVhdDifferencing(string parentPath, string childPath)
    {
        using var parent = new VhdDisk(parentPath, FileAccess.Read);
        using var child = parent.CreateDifferencingDisk(childPath);
    }

    private static void WriteContent(VirtualDisk disk, byte[] diskContent)
    {
        disk.Content.Position = 0;
        disk.Content.Write(diskContent, 0, diskContent.Length);
    }

    private static long Capacity(byte[] diskContent)
    {
        var rounded = (diskContent.Length + SectorSize - 1) / SectorSize * (long)SectorSize;
        return rounded == 0 ? SectorSize : rounded;
    }
}
