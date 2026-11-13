using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Integration tests for cooperative cancellation through catalog scan operations (#432).
/// </summary>
public sealed class CancellationScanTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-cancel-{Guid.NewGuid():N}");

    // ──────────────────────────────────────────────────────────────
    // FolderCatalogScanner — ScanDetailed returns Canceled outcome
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void FolderScanDetailed_CanceledToken_ReturnsCanceledOutcome_AndNoDocuments()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "file.txt"), length: 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(new FolderScanOptions(root), cancellationToken: cts.Token);

        Assert.Equal(ScanOutcome.Canceled, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public void FolderScan_CanceledToken_ThrowsOperationCanceledException()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "file.txt"), length: 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new FolderCatalogScanner(fs);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            scanner.Scan(new FolderScanOptions(root), cancellationToken: cts.Token));
    }

    [Fact]
    public void FolderScanDetailed_CanceledDuringTraversal_ReturnsCanceledOutcome_AndCancellationIsLogged()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        // Enumerating files in root will trigger cancellation via the failure hook
        for (var i = 0; i < 5; i++)
        {
            fs.AddFile(Path.Combine(root, $"file{i}.txt"), length: i + 1);
        }

        using var cts = new CancellationTokenSource();
        // Cancel synchronously on the first enumerate call
        fs.OnEnumerateFiles += _ => cts.Cancel();

        var progress = new AddingProgressDialogViewModel();
        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(new FolderScanOptions(root), progress, cts.Token);

        Assert.Equal(ScanOutcome.Canceled, result.Outcome);
        // A cancellation warning must have been emitted — never a success completion message
        var log = progress.GetFullLogText();
        Assert.Contains("canceled", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scanning complete", log, StringComparison.OrdinalIgnoreCase);
        progress.Dispose();
    }

    // ──────────────────────────────────────────────────────────────
    // MediaCatalogScanner — ScanDetailed returns Canceled outcome
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void MediaScanDetailed_CanceledToken_ReturnsCanceledOutcome_AndNoDocuments()
    {
        var driveRoot = Path.Combine(root, "D");
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(driveRoot);
        fs.AddFile(Path.Combine(driveRoot, "track.mp3"), length: 10);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new StubDriveProvider(new MediaDriveInfo(
            Name: "D:", VolumeLabel: "Audio", RootDirectory: driveRoot,
            DriveType: DriveType.CDRom, IsReady: true,
            AvailableFreeSpace: 0, TotalFreeSpace: 0, TotalSize: 100, DriveFormat: "CDFS"));

        var scanner = new MediaCatalogScanner(provider, fs);
        var result = scanner.ScanDetailed(new MediaScanOptions(driveRoot), cancellationToken: cts.Token);

        Assert.Equal(ScanOutcome.Canceled, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Documents);
    }

    // ──────────────────────────────────────────────────────────────
    // AddingProgressDialogViewModel — Cancel command wires cancellation
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddingProgressDialogViewModel_CancelScan_SetsIsCanceled_AndCancelsToken()
    {
        using var vm = new AddingProgressDialogViewModel();
        Assert.False(vm.IsCanceled);
        Assert.True(vm.IsCancelEnabled);
        Assert.False(vm.CancellationToken.IsCancellationRequested);

        vm.CancelScanCommand.Execute(null);

        Assert.True(vm.IsCanceled);
        Assert.False(vm.IsCancelEnabled);
        Assert.True(vm.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void AddingProgressDialogViewModel_CancelScan_IsIdempotent()
    {
        using var vm = new AddingProgressDialogViewModel();
        vm.CancelScanCommand.Execute(null);
        vm.CancelScanCommand.Execute(null); // must not throw
        Assert.True(vm.IsCanceled);
    }

    [Fact]
    public void AddingProgressDialogViewModel_CancelScan_LogsWarning()
    {
        using var vm = new AddingProgressDialogViewModel();
        vm.CancelScanCommand.Execute(null);

        Assert.True(vm.WarningCount >= 1);
        var log = vm.GetFullLogText();
        Assert.Contains("cancel", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FolderScanDetailed_UsesVmCancellationToken_CancelStopsScan_NothingPublished()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        for (var i = 0; i < 3; i++)
        {
            fs.AddFile(Path.Combine(root, $"file{i}.txt"), length: 1);
        }

        using var vm = new AddingProgressDialogViewModel();
        // Wire: cancel as soon as enumeration starts
        fs.OnEnumerateFiles += _ => vm.CancelScanCommand.Execute(null);

        var scanner = new FolderCatalogScanner(fs);
        var result = scanner.ScanDetailed(new FolderScanOptions(root), vm, vm.CancellationToken);

        Assert.Equal(ScanOutcome.Canceled, result.Outcome);
        Assert.Empty(result.Documents);
        Assert.True(vm.IsCanceled);
    }

    // ──────────────────────────────────────────────────────────────
    // CancellationTokenSource disposal — no double-dispose
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddingProgressDialogViewModel_Dispose_IsIdempotent()
    {
        var vm = new AddingProgressDialogViewModel();
        vm.Dispose();
        vm.Dispose(); // must not throw
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private sealed class StubDriveProvider(MediaDriveInfo drive) : IMediaDriveProvider
    {
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => [drive];

        public MediaDriveInfo? GetDrive(string drivePathOrName) =>
            drive.Name == drivePathOrName || drive.RootDirectory == drivePathOrName ? drive : null;
    }
}
