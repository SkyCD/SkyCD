using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class MediaCatalogScannerTests : IDisposable
{
    private readonly string testDirectory;

    public MediaCatalogScannerTests()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), $"skycd-media-scan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private sealed class FakeDriveProvider(IReadOnlyList<MediaDriveInfo> drives) : IMediaDriveProvider
    {
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => drives;
        public MediaDriveInfo? GetDrive(string drivePathOrName) => drives.FirstOrDefault(d => d.Name == drivePathOrName || d.RootDirectory == drivePathOrName);
    }

    [Fact]
    public void Scan_NonExistentDrive_ThrowsMediaDriveNotFoundException()
    {
        var provider = new FakeDriveProvider([]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions("Z:\\");

        var ex = Assert.Throws<MediaDriveNotFoundException>(() => scanner.Scan(options));
        Assert.Contains("Z:\\", ex.Message);
    }

    [Fact]
    public void Scan_NotReadyDrive_ThrowsMediaDriveNotReadyException()
    {
        var notReadyDrive = new MediaDriveInfo("D:\\", null, "D:\\", DriveType.CDRom, false, 0, 0, 0);
        var provider = new FakeDriveProvider([notReadyDrive]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions("D:\\");

        var ex = Assert.Throws<MediaDriveNotReadyException>(() => scanner.Scan(options));
        Assert.Contains("D:\\", ex.Message);
    }

    [Fact]
    public void Scan_InaccessibleRoot_ThrowsMediaDriveInaccessibleException()
    {
        var nonExistentPath = Path.Combine(testDirectory, "MissingDriveFolder");
        var drive = new MediaDriveInfo("D:\\", "Label", nonExistentPath, DriveType.CDRom, true, 0, 0, 700_000_000);
        var provider = new FakeDriveProvider([drive]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions("D:\\");

        var ex = Assert.Throws<MediaDriveInaccessibleException>(() => scanner.Scan(options));
        Assert.Contains("D:\\", ex.Message);
    }

    [Fact]
    public void Scan_ValidDrive_WithoutMediaInfo_ReturnsMediaHierarchy()
    {
        File.WriteAllText(Path.Combine(testDirectory, "track1.cda"), "audio-data");
        var subDir = Path.Combine(testDirectory, "DataTrack");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "readme.txt"), "disc info");

        var drive = new MediaDriveInfo("D:\\", "MusicDisc", testDirectory, DriveType.CDRom, true, 0, 0, 700_000_000);
        var provider = new FakeDriveProvider([drive]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions(
            DrivePathOrName: "D:\\",
            MediaName: "CustomDiscTitle",
            IncludeMediaInfo: false);

        var documents = scanner.Scan(options);

        var rootDoc = Assert.Single(documents, doc => doc.ParentId == null);
        Assert.Equal("CustomDiscTitle", rootDoc.Name);
        Assert.Equal(CatalogDocumentType.Media, rootDoc.Type);
        Assert.Equal(2, rootDoc.ChildrenCount); // track1.cda + DataTrack
        Assert.False(rootDoc.Properties.ContainsKey("VolumeLabel"));
        Assert.False(rootDoc.Properties.ContainsKey("TotalSize"));

        var trackDoc = Assert.Single(documents, doc => doc.Name == "track1.cda");
        Assert.Equal(CatalogDocumentType.File, trackDoc.Type);
        Assert.Equal(rootDoc.Id, trackDoc.ParentId);

        var subDirDoc = Assert.Single(documents, doc => doc.Name == "DataTrack");
        Assert.Equal(CatalogDocumentType.Folder, subDirDoc.Type);
        Assert.Equal(rootDoc.Id, subDirDoc.ParentId);
        Assert.Equal(1, subDirDoc.ChildrenCount);

        var readmeDoc = Assert.Single(documents, doc => doc.Name == "readme.txt");
        Assert.Equal(CatalogDocumentType.File, readmeDoc.Type);
        Assert.Equal(subDirDoc.Id, readmeDoc.ParentId);
    }

    [Fact]
    public void Scan_ValidDrive_WithMediaInfo_PopulatesDriveMetadata()
    {
        File.WriteAllText(Path.Combine(testDirectory, "content.dat"), "12345");

        var drive = new MediaDriveInfo("E:\\", "USB_MEDIA", testDirectory, DriveType.Removable, true, 500_000_000, 500_000_000, 16_000_000_000, "FAT32");
        var provider = new FakeDriveProvider([drive]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions(
            DrivePathOrName: "E:\\",
            IncludeMediaInfo: true);

        var documents = scanner.Scan(options);

        var rootDoc = Assert.Single(documents, doc => doc.ParentId == null);
        Assert.Equal("USB_MEDIA", rootDoc.Name);
        Assert.Equal("E:\\", rootDoc.Properties["Title"]?.ToString());
        Assert.Equal("USB_MEDIA", rootDoc.Properties["VolumeLabel"]?.ToString());
        Assert.Equal(testDirectory, rootDoc.Properties["RootDirectory"]?.ToString());
        Assert.Equal("500000000", rootDoc.Properties["AvailableFreeSpace"]?.ToString());
        Assert.Equal("500000000", rootDoc.Properties["TotalFreeSpace"]?.ToString());
        Assert.Equal("16000000000", rootDoc.Properties["TotalSize"]?.ToString());
        Assert.Equal("Removable", rootDoc.Properties["DriveType"]?.ToString());
        Assert.Equal("FAT32", rootDoc.Properties["DriveFormat"]?.ToString());
    }

    [Fact]
    public void Scan_ValidDrive_WithExtendedInfo_PopulatesFileAndFolderProperties()
    {
        var filePath = Path.Combine(testDirectory, "file.txt");
        File.WriteAllText(filePath, "sample");

        var drive = new MediaDriveInfo("D:\\", "Disc", testDirectory, DriveType.CDRom, true, 0, 0, 700_000_000);
        var provider = new FakeDriveProvider([drive]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions(
            DrivePathOrName: "D:\\",
            IncludeExtendedInfo: true);

        var documents = scanner.Scan(options);

        var fileDoc = documents.First(doc => doc.Type == CatalogDocumentType.File);
        Assert.True(fileDoc.Properties.ContainsKey("FullName"));
        Assert.True(fileDoc.Properties.ContainsKey("Size"));
        Assert.True(fileDoc.Properties.ContainsKey("Attributes"));
        Assert.True(fileDoc.Properties.ContainsKey("IsReadOnly"));
    }

    [Fact]
    public void Scan_ReportsProgress()
    {
        File.WriteAllText(Path.Combine(testDirectory, "item.txt"), "data");

        var drive = new MediaDriveInfo("D:\\", "Disc", testDirectory, DriveType.CDRom, true, 0, 0, 700_000_000);
        var provider = new FakeDriveProvider([drive]);
        var scanner = new MediaCatalogScanner(provider);
        var options = new MediaScanOptions("D:\\");

        var progressReports = new List<(string Status, int Percentage)>();
        var progress = new Progress<(string Status, int Percentage)>(p => progressReports.Add(p));

        scanner.Scan(options, progress);

        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => p.Percentage == 100);
    }
}
