using System;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class ScanTraversalResilienceTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-scan-{Guid.NewGuid():N}");

    [Fact]
    public void FolderScan_DetectsSymlinkCycles_AndSkipsWithoutHanging()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddDirectory(Path.Combine(root, "child"));
        fs.AddFile(Path.Combine(root, "child", "file.txt"), length: 4);
        fs.LinkDirectory(Path.Combine(root, "child", "loop"), root);

        var progress = new AddingProgressDialogViewModel();
        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    LinkFollowPolicy = LinkFollowPolicy.FollowDirectoryLinksOnce
                }),
            progress);

        Assert.Contains(result.Documents, d => d.Name == "file.txt");
        Assert.True(result.Statistics.Skipped >= 1);
        Assert.True(progress.HasSkipped);
        Assert.Contains(progress.LogEntries, e => e.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FolderScan_NeverFollowLinks_DoesNotDescendIntoDirectoryLinks()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"skycd-outside-{Guid.NewGuid():N}");
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddDirectory(outside);
        fs.AddFile(Path.Combine(outside, "only-via-link.txt"), length: 2);
        fs.LinkDirectory(Path.Combine(root, "link"), outside);

        var scanner = new FolderCatalogScanner(fs);
        var neverFollow = scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    LinkFollowPolicy = LinkFollowPolicy.NeverFollowLinks
                }));

        Assert.DoesNotContain(neverFollow.Documents, d => d.Name == "only-via-link.txt");
        Assert.True(neverFollow.Statistics.Skipped >= 1);

        var followOnce = scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    LinkFollowPolicy = LinkFollowPolicy.FollowDirectoryLinksOnce
                }));

        Assert.Contains(followOnce.Documents, d => d.Name == "only-via-link.txt");
    }

    [Fact]
    public void FolderScan_PermissionFailureOnChild_IsFailedItem_NotFatal()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddDirectory(Path.Combine(root, "ok"));
        fs.AddFile(Path.Combine(root, "ok", "a.txt"), length: 3);
        fs.AddDirectory(Path.Combine(root, "locked"));
        fs.SetEnumerateFailure(
            Path.Combine(root, "locked"),
            () => new UnauthorizedAccessException("Access denied"),
            failTimes: 5);

        var progress = new AddingProgressDialogViewModel();
        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(new FolderScanOptions(root), progress);

        Assert.Contains(result.Documents, d => d.Name == "a.txt");
        Assert.True(result.Statistics.Failed >= 1);
        Assert.True(progress.HasFailed);
        Assert.Contains(progress.LogEntries, e => e.Level == OperationLogLevel.ItemFailed);
    }

    [Fact]
    public void FolderScan_DisconnectThenReconnect_RetriesAndSucceeds()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "after-reconnect.bin"), length: 8);
        fs.SetEnumerateFailure(
            root,
            () => new IOException("Device not ready"),
            failTimes: 2);

        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    MaxRetries = 3,
                    RetryDelay = TimeSpan.FromMilliseconds(1)
                }));

        Assert.Contains(result.Documents, d => d.Name == "after-reconnect.bin");
        Assert.True(fs.EnumerateFilesCallCount >= 3);
    }

    [Fact]
    public void FolderScan_RetryExhaustionOnRoot_IsFatal()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "never-seen.txt"), length: 1);
        fs.SetEnumerateFailure(
            root,
            () => new IOException("Media disconnected"),
            failTimes: 20);

        var scanner = new FolderCatalogScanner(fs);
        var ex = Assert.Throws<ScanRetryExhaustedException>(() =>
            scanner.Scan(
                new FolderScanOptions(
                    FolderPath: root,
                    Traversal: new ScanTraversalOptions
                    {
                        MaxRetries = 1,
                        RetryDelay = TimeSpan.Zero
                    })));

        Assert.Equal("enumerate-files", ex.Stage);
        Assert.Contains(root, ex.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FolderScan_PropagatesCancellationDuringRetryWait()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.SetEnumerateFailure(
            root,
            () => new IOException("Temporary disconnect"),
            failTimes: 50);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(30));

        var scanner = new FolderCatalogScanner(fs);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            scanner.Scan(
                new FolderScanOptions(
                    FolderPath: root,
                    Traversal: new ScanTraversalOptions
                    {
                        MaxRetries = 100,
                        RetryDelay = TimeSpan.FromMilliseconds(50)
                    }),
                progress: null,
                cancellationToken: cts.Token));
    }

    [Fact]
    public void FolderScan_HiddenSystemAndExclusionPolicies_PreserveCounts()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "visible.txt"), length: 2);
        fs.AddFile(Path.Combine(root, "secret.tmp"), length: 2);
        fs.AddFile(Path.Combine(root, "desktop.ini"), length: 2, attributes: FileAttributes.Hidden | FileAttributes.System);
        fs.AddFile(Path.Combine(root, "Thumbs.db"), length: 2, attributes: FileAttributes.Hidden);

        var progress = new AddingProgressDialogViewModel();
        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    HiddenSystemFilePolicy = HiddenSystemFilePolicy.SkipHiddenAndSystem,
                    ExclusionPatterns = ["*.tmp"]
                }),
            progress);

        Assert.Contains(result.Documents, d => d.Name == "visible.txt");
        Assert.DoesNotContain(result.Documents, d => d.Name == "secret.tmp");
        Assert.DoesNotContain(result.Documents, d => d.Name == "desktop.ini");
        Assert.DoesNotContain(result.Documents, d => d.Name == "Thumbs.db");
        Assert.Equal(3, result.Statistics.Skipped);
        Assert.Equal(2, result.Statistics.Discovered); // root + visible.txt
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Equal(3, progress.SkippedCount);
    }

    [Fact]
    public void FolderScan_LargeDirectoryTree_CompletesWithAccurateCounts()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);

        const int folderCount = 40;
        const int filesPerFolder = 5;
        for (var i = 0; i < folderCount; i++)
        {
            var dir = Path.Combine(root, $"dir-{i:D3}");
            fs.AddDirectory(dir);
            for (var j = 0; j < filesPerFolder; j++)
            {
                fs.AddFile(Path.Combine(dir, $"file-{j:D2}.dat"), length: j + 1);
            }
        }

        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    MaxConcurrency = 4,
                    MaxRetries = 0,
                    RetryDelay = TimeSpan.Zero
                }));

        var expectedFiles = folderCount * filesPerFolder;
        var expectedFolders = folderCount;
        Assert.Equal(1 + expectedFolders + expectedFiles, result.Documents.Count);
        Assert.Equal(1 + expectedFolders + expectedFiles, result.Statistics.Discovered);
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Equal(0, result.Statistics.Skipped);
    }

    [Fact]
    public void FolderScan_FailedItem_IsReportedWithoutCredentials()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddDirectory(Path.Combine(root, "ftp-area"));
        fs.SetEnumerateFailure(
            Path.Combine(root, "ftp-area"),
            () => new IOException("ftp://user:p@ssw0rd@host/failed listing"),
            failTimes: 20);

        var progress = new AddingProgressDialogViewModel();
        var scanner = new FolderCatalogScanner(fs);
        scanner.ScanDetailed(
            new FolderScanOptions(
                FolderPath: root,
                Traversal: new ScanTraversalOptions
                {
                    MaxRetries = 0,
                    RetryDelay = TimeSpan.Zero
                }),
            progress);

        var logText = progress.GetFullLogText();
        Assert.DoesNotContain("p@ssw0rd", logText);
        Assert.Contains("user:***@", logText);
        Assert.True(progress.FailedCount >= 1);
    }

    [Fact]
    public void MediaScan_UsesSharedTraversal_AndRecordsFailures()
    {
        var fs = new FakeScanFileSystem();
        var driveRoot = Path.Combine(root, "D");
        fs.AddDirectory(driveRoot);
        fs.AddFile(Path.Combine(driveRoot, "track.mp3"), length: 10);
        fs.AddDirectory(Path.Combine(driveRoot, "private"));
        fs.SetEnumerateFailure(
            Path.Combine(driveRoot, "private"),
            () => new UnauthorizedAccessException("denied"),
            failTimes: 1);

        var provider = new StubMediaDriveProvider(new MediaDriveInfo(
            Name: "D:",
            VolumeLabel: "Audio",
            RootDirectory: driveRoot,
            DriveType: DriveType.CDRom,
            IsReady: true,
            AvailableFreeSpace: 0,
            TotalFreeSpace: 0,
            TotalSize: 100,
            DriveFormat: "CDFS"));

        var progress = new AddingProgressDialogViewModel();
        var scanner = new MediaCatalogScanner(provider, fs);
        var result = scanner.ScanDetailed(new MediaScanOptions(driveRoot), progress);

        Assert.Contains(result.Documents, d => d.Name == "track.mp3");
        Assert.True(result.Statistics.Failed >= 1);
        Assert.True(progress.HasFailed);
    }

    private sealed class StubMediaDriveProvider(MediaDriveInfo drive) : IMediaDriveProvider
    {
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public System.Collections.Generic.IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => [drive];

        public MediaDriveInfo? GetDrive(string drivePathOrName) =>
            drive.Name == drivePathOrName || drive.RootDirectory == drivePathOrName ? drive : null;
    }
}
