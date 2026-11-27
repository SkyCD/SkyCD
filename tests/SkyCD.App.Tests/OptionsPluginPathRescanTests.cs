using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Contract of the Plugins → Settings folder field: typing never scans, only a commit (Enter, Browse,
/// lost focus or OK) runs discovery, a superseded scan is cancelled before it can change the runtime,
/// a folder that does not exist shows an inline hint and leaves the list alone, and Cancel restores the
/// folder the dialog opened with.
/// </summary>
public sealed class OptionsPluginPathRescanTests : IDisposable
{
    private readonly TestWorkspace workspace = new();
    private readonly string storedFolder;
    private readonly string typedFolder;
    private readonly string otherFolder;
    private readonly string missingFolder;

    private string root => workspace.Root;

    public OptionsPluginPathRescanTests()
    {
        storedFolder = Path.Combine(root, "stored");
        typedFolder = Path.Combine(root, "typed");
        otherFolder = Path.Combine(root, "other");
        missingFolder = Path.Combine(root, "missing");
        Directory.CreateDirectory(storedFolder);
        Directory.CreateDirectory(typedFolder);
        Directory.CreateDirectory(otherFolder);
    }

    public void Dispose() => workspace.Dispose();

    [Fact]
    public async Task OpeningTheDialog_SeedsTheStoredFolderWithoutScanningOrHinting()
    {
        using var vm = CreateViewModel(out var scanner);

        vm.InitializePluginPath(storedFolder);
        await Task.Yield();

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.False(vm.HasPluginPathHint);
        Assert.False(vm.IsPluginPathScanning);
        Assert.Empty(scanner.ScanPaths);
    }

    [Fact]
    public async Task TypingDeletingAndPasting_NeverScan()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);

        foreach (var text in new[]
                 {
                     typedFolder[..^3],
                     typedFolder[..^2],
                     typedFolder[..^1],
                     typedFolder,
                     typedFolder[..^4],
                     typedFolder
                 })
        {
            vm.PluginPath = text;
            await Task.Yield();
        }

        Assert.Empty(scanner.ScanPaths);
        Assert.False(vm.IsPluginPathScanning);
    }

    [Fact]
    public async Task Enter_CommitsTheTypedFolderExactlyOnce()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);

        vm.PluginPath = typedFolder;
        await vm.RescanPluginPathCommand.ExecuteAsync(null);

        // A second commit of the same folder has nothing left to discover.
        await vm.RescanPluginPathCommand.ExecuteAsync(null);

        Assert.Equal([typedFolder], scanner.ScanPaths);
        Assert.False(vm.IsPluginPathScanning);
    }

    [Fact]
    public async Task BrowsePick_CommitsThePickedFolderExactlyOnce()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);

        await vm.BrowsePluginPathAsync(_ => Task.FromResult<string?>(typedFolder));

        Assert.Equal(typedFolder, vm.PluginPath);
        Assert.Equal([typedFolder], scanner.ScanPaths);
    }

    [Fact]
    public async Task TypingDuringAScan_CancelsItWithoutApplyingTheDraftFolder()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.Behavior = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };

        vm.PluginPath = typedFolder;
        var scan = vm.RescanPluginPathCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.IsPluginPathScanning);

        vm.PluginPath = otherFolder;
        await scan;

        Assert.True(Assert.Single(scanner.Tokens).IsCancellationRequested);
        Assert.False(vm.IsPluginPathScanning);
    }

    [Fact]
    public async Task ANewerCommit_CancelsTheEarlierScan()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.Behavior = async (path, token) =>
        {
            if (path == typedFolder)
            {
                firstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return;
            }

            vm.MarkPluginPathScanned(path);
        };

        vm.PluginPath = typedFolder;
        var first = vm.RescanPluginPathCommand.ExecuteAsync(null);
        await firstStarted.Task;

        vm.PluginPath = otherFolder;
        await vm.RescanPluginPathCommand.ExecuteAsync(null);
        await first;

        Assert.Equal([typedFolder, otherFolder], scanner.ScanPaths);
        Assert.True(scanner.Tokens[0].IsCancellationRequested);
        Assert.False(scanner.Tokens[1].IsCancellationRequested);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task MissingFolder_ShowsAnInlineHintAndKeepsTheScannedList(string scenario)
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        vm.SetPlugins([new OptionsPluginItem("Sample", "Author", "sample v1.0", id: "sample")]);

        vm.PluginPath = scenario == "missing" ? missingFolder : string.Empty;
        await vm.RescanPluginPathCommand.ExecuteAsync(null);

        Assert.True(vm.HasPluginPathHint);
        Assert.DoesNotContain("core.ui.", vm.PluginPathHint);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.PluginPathDirectoryMissing), vm.PluginPathHint);
        Assert.Empty(scanner.ScanPaths);
        Assert.Single(vm.Plugins);
    }

    [Fact]
    public async Task AValidFolder_ClearsTheHintAgainAndScansOnCommit()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = missingFolder;
        Assert.True(vm.HasPluginPathHint);

        vm.PluginPath = typedFolder;
        Assert.False(vm.HasPluginPathHint);
        Assert.Empty(scanner.ScanPaths);

        await vm.RescanPluginPathCommand.ExecuteAsync(null);

        Assert.Equal([typedFolder], scanner.ScanPaths);
    }

    [Fact]
    public async Task Cancel_RestoresTheStoredFolderAndRescansItWhenTheRuntimeShowsAPreview()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        await vm.RescanPluginPathCommand.ExecuteAsync(null);

        await vm.RevertPluginPathPreviewAsync();

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.Equal([typedFolder, storedFolder], scanner.ScanPaths);
    }

    [Fact]
    public async Task Cancel_WithoutAPreviewScan_OnlyPutsTheFieldBack()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;

        await vm.RevertPluginPathPreviewAsync();

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.Empty(scanner.ScanPaths);
        Assert.False(vm.HasPluginPathHint);
    }

    [Fact]
    public async Task Cancel_CancelsADraftScanBeforeItCanChangeTheRuntime()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.Behavior = async (path, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            vm.MarkPluginPathScanned(path);
        };

        vm.PluginPath = typedFolder;
        var scan = vm.RescanPluginPathCommand.ExecuteAsync(null);
        await started.Task;

        // Cancel must abandon the draft even if the user never pressed Enter: a lost focus already did.
        var revert = vm.RevertPluginPathPreviewAsync();
        await scan;
        await revert;

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.True(Assert.Single(scanner.Tokens).IsCancellationRequested);
        Assert.Equal([typedFolder], scanner.ScanPaths);
        Assert.False(vm.IsPluginPathScanning);
    }

    [Fact]
    public async Task ScanningState_TracksTheRunningPass()
    {
        using var vm = CreateViewModel(out var scanner);
        vm.InitializePluginPath(storedFolder);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.Behavior = async (path, _) =>
        {
            started.TrySetResult();
            await release.Task;
            vm.MarkPluginPathScanned(path);
        };

        vm.PluginPath = typedFolder;
        var scan = vm.RescanPluginPathCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.IsPluginPathScanning);

        release.SetResult();
        await scan;
        Assert.False(vm.IsPluginPathScanning);
    }

    private static OptionsDialogViewModel CreateViewModel(out RecordingScanner scanner)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        scanner = new RecordingScanner(vm);
        vm.SetPluginPathScanner(scanner.ScanAsync);
        return vm;
    }

    private sealed class RecordingScanner(OptionsDialogViewModel owner)
    {
        public List<string> ScanPaths { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Func<string, CancellationToken, Task>? Behavior { get; set; }

        public Task ScanAsync(string path, CancellationToken cancellationToken)
        {
            ScanPaths.Add(path);
            Tokens.Add(cancellationToken);
            if (Behavior is not null)
            {
                return Behavior(path, cancellationToken);
            }

            owner.MarkPluginPathScanned(path);
            return Task.CompletedTask;
        }
    }
}
