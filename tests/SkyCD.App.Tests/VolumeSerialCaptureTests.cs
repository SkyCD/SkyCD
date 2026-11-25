using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.SDK.Capabilities.Volume;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class VolumeSerialCaptureTests : IDisposable
{
    private readonly string testDirectory;

    public VolumeSerialCaptureTests()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), $"skycd-volume-serial-capture-{Guid.NewGuid():N}");
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
        public MediaDriveInfo? GetDrive(string drivePathOrName) => drives.FirstOrDefault(
            d => d.Name == drivePathOrName || d.RootDirectory == drivePathOrName);
    }

    private sealed class FakeVolumeProvider : IVolumeMetadataProvider
    {
        public IReadOnlyList<MediaMetadataValue> Values { get; set; } = [];
        public Exception? Failure { get; set; }
        public Action? OnCapture { get; set; }
        public VolumeMediaContext? LastContext { get; private set; }
        public int CaptureCount { get; private set; }

        public IReadOnlyList<MediaMetadataValue> CaptureVolumeMetadata(VolumeMediaContext volume,
            CancellationToken cancellationToken)
        {
            LastContext = volume;
            CaptureCount++;
            cancellationToken.ThrowIfCancellationRequested();
            OnCapture?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }

            return Values;
        }
    }

    private static MediaMetadataValue Serial(string text) =>
        new("VolumeSerialNumber", MediaMetadataType.Text, Text: text);

    private (CatalogScanResult Result, FakeVolumeProvider Volume) Scan(
        FakeVolumeProvider volume, bool includeMediaInfo = true, string? targetParentId = null)
    {
        File.WriteAllText(Path.Combine(testDirectory, "track01.cda"), "audio");
        var drive = new MediaDriveInfo("D:\\", "MusicDisc", testDirectory, DriveType.CDRom, true, 0, 0, 700_000_000, "CDFS");
        var scanner = new MediaCatalogScanner(new FakeDriveProvider([drive]), volumeMetadata: volume);
        var options = new MediaScanOptions(
            DrivePathOrName: "D:\\",
            MediaName: "MusicDisc",
            TargetParentId: targetParentId,
            IncludeMediaInfo: includeMediaInfo);
        return (scanner.ScanDetailed(options), volume);
    }

    [Fact]
    public void Scan_NewMedia_StoresVolumeSerialOnRoot()
    {
        var volume = new FakeVolumeProvider { Values = [Serial("1234-ABCD")] };

        var (result, _) = Scan(volume);

        var root = Assert.Single(result.Documents, doc => doc.ParentId is null);
        Assert.Null(root.ParentId);
        Assert.Equal("1234-ABCD", root.Properties["VolumeSerialNumber"]?.ToString());
        Assert.Equal(testDirectory, volume.LastContext?.RootPath);
        Assert.Equal("D:\\", volume.LastContext?.Name);
        Assert.Equal("MusicDisc", volume.LastContext?.VolumeLabel);
        Assert.Equal("CDFS", volume.LastContext?.DriveFormat);
    }

    [Fact]
    public void Scan_BeneathSelectedCatalogItem_StoresVolumeSerialOnRoot()
    {
        var volume = new FakeVolumeProvider { Values = [Serial("1234-ABCD")] };

        var (result, _) = Scan(volume, targetParentId: "library-1");

        var root = Assert.Single(result.Documents, doc => doc.Type == CatalogDocumentType.Media);
        Assert.Equal("library-1", root.ParentId);
        Assert.Equal("1234-ABCD", root.Properties["VolumeSerialNumber"]?.ToString());
    }

    [Fact]
    public void Scan_WithoutMediaInfo_DoesNotCaptureVolumeSerial()
    {
        var volume = new FakeVolumeProvider { Values = [Serial("1234-ABCD")] };

        var (result, _) = Scan(volume, includeMediaInfo: false);

        var root = Assert.Single(result.Documents, doc => doc.ParentId is null);
        Assert.False(root.Properties.ContainsKey("VolumeSerialNumber"));
        Assert.False(root.Properties.ContainsKey("VolumeLabel"));
        Assert.Equal(0, volume.CaptureCount);
    }

    [Fact]
    public void Scan_ZeroSerial_StoresGenuineZero()
    {
        var volume = new FakeVolumeProvider { Values = [Serial("0000-0000")] };

        var (result, _) = Scan(volume);

        var root = Assert.Single(result.Documents, doc => doc.ParentId is null);
        Assert.Equal("0000-0000", root.Properties["VolumeSerialNumber"]?.ToString());
    }

    [Fact]
    public void Scan_UnavailableSerial_WritesNoPropertyOrPlaceholder()
    {
        var volume = new FakeVolumeProvider { Values = [] };

        var (result, _) = Scan(volume);

        var root = Assert.Single(result.Documents, doc => doc.ParentId is null);
        Assert.False(root.Properties.ContainsKey("VolumeSerialNumber"));
    }

    [Fact]
    public void Scan_ProviderFailure_DoesNotBreakScan()
    {
        var volume = new FakeVolumeProvider { Failure = new IOException("volume capture failed") };

        var (result, _) = Scan(volume);

        var root = Assert.Single(result.Documents, doc => doc.ParentId is null);
        Assert.False(root.Properties.ContainsKey("VolumeSerialNumber"));
        Assert.Contains(result.Documents, doc => doc.Name == "track01.cda");
    }

    [Fact]
    public void Scan_ProviderCancellation_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        var volume = new FakeVolumeProvider();
        volume.OnCapture = () => cancellation.Cancel();
        volume.Failure = new OperationCanceledException(cancellation.Token);

        var drive = new MediaDriveInfo("D:\\", "MusicDisc", testDirectory, DriveType.CDRom, true, 0, 0, 700_000_000);
        var scanner = new MediaCatalogScanner(new FakeDriveProvider([drive]), volumeMetadata: volume);

        Assert.ThrowsAny<OperationCanceledException>(() => scanner.ScanDetailed(
            new MediaScanOptions(DrivePathOrName: "D:\\", IncludeMediaInfo: true),
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Scan_MultipleValues_AreAllStoredOnRoot()
    {
        var volume = new FakeVolumeProvider
        {
            Values =
            [
                Serial("1234-ABCD"),
                new("VolumeFilesystem", MediaMetadataType.Text, Text: "CDFS")
            ]
        };

        var (result, _) = Scan(volume);

        var root = Assert.Single(result.Documents, doc => doc.ParentId is null);
        Assert.Equal("1234-ABCD", root.Properties["VolumeSerialNumber"]?.ToString());
        Assert.Equal("CDFS", root.Properties["VolumeFilesystem"]?.ToString());
    }
}
