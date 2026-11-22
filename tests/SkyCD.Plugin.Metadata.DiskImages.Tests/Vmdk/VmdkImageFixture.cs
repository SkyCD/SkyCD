using System;
using System.IO;
using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using DiscUtils.Vmdk;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>
/// A real VMDK image with a FAT partition, created through DiscUtils so plugin behavior is exercised
/// against container data rather than mock objects.
/// </summary>
internal sealed class VmdkImageFixture : IDisposable
{
    private const long CapacityBytes = 16 * 1024 * 1024;

    private VmdkImageFixture(string directory, string imagePath)
    {
        Directory = directory;
        ImagePath = imagePath;
    }

    public string Directory { get; }

    public string ImagePath { get; }

    public static VmdkImageFixture Create(
        params (string Path, string Content)[] files) =>
        Create(DiskCreateType.MonolithicSparse, files);

    public static VmdkImageFixture Create(
        DiskCreateType createType,
        params (string Path, string Content)[] files)
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("skycd-vmdk-test-").FullName;
        var imagePath = Path.Combine(directory, "disk.vmdk");

        using (var disk = Disk.Initialize(imagePath, CapacityBytes, createType))
        {
            BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
            using var fileSystem = FatFileSystem.FormatPartition(disk, 0, "DATA");
            foreach (var (path, content) in files)
            {
                var rawPath = path.Replace('/', '\\');
                var parent = Path.GetDirectoryName(path.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(parent))
                {
                    fileSystem.CreateDirectory(parent.Replace('/', '\\'));
                }

                using var writer = new StreamWriter(
                    fileSystem.OpenFile(rawPath, FileMode.Create, FileAccess.Write),
                    new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return new VmdkImageFixture(directory, imagePath);
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }
}
