using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Contract of the Plugins → Settings folder field: a typing pause, Enter or a lost focus rescans the
/// runtime, a folder that does not exist shows an inline hint and leaves the list alone, and Cancel
/// restores the folder the dialog opened with.
/// </summary>
public sealed class OptionsPluginPathRescanTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "skycd-plugin-path-" + Guid.NewGuid());
    private readonly string storedFolder;
    private readonly string typedFolder;
    private readonly string missingFolder;

    public OptionsPluginPathRescanTests()
    {
        storedFolder = Path.Combine(root, "stored");
        typedFolder = Path.Combine(root, "typed");
        missingFolder = Path.Combine(root, "missing");
        Directory.CreateDirectory(storedFolder);
        Directory.CreateDirectory(typedFolder);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void OpeningTheDialog_SeedsTheStoredFolderWithoutScanningOrHinting()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);

        vm.InitializePluginPath(storedFolder);
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 2);

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.False(vm.HasPluginPathHint);
        Assert.Empty(scans);
    }

    [Fact]
    public void TypingPause_RescansTheTypedFolderExactlyOnce()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);

        vm.PluginPath = typedFolder;
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Empty(scans);

        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal([typedFolder], scans);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 4);
        Assert.Single(scans);
    }

    [Fact]
    public void KeystrokeByKeystrokeEdits_SupersedeThePendingScanInsteadOfQueueingOne()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);

        foreach (var text in new[]
                 {
                     typedFolder[..^3],
                     typedFolder[..^2],
                     typedFolder[..^1],
                     typedFolder
                 })
        {
            vm.PluginPath = text;
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Empty(scans);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);

        Assert.Equal([typedFolder], scans);
    }

    [Fact]
    public void EnterOrLostFocus_RescansImmediatelyAndDropsThePause()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);

        vm.PluginPath = typedFolder;
        vm.RescanPluginPathCommand.Execute(null);

        Assert.Equal([typedFolder], scans);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 2);

        Assert.Single(scans);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    public void MissingFolder_ShowsAnInlineHintAndKeepsTheScannedList(string scenario)
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.SetPlugins([new OptionsPluginItem("Sample", "Author", "sample v1.0", id: "sample")]);

        vm.PluginPath = scenario == "missing" ? missingFolder : string.Empty;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 2);

        Assert.True(vm.HasPluginPathHint);
        Assert.DoesNotContain("core.ui.", vm.PluginPathHint);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.PluginPathDirectoryMissing), vm.PluginPathHint);
        Assert.Empty(scans);
        Assert.Single(vm.Plugins);
    }

    [Fact]
    public void AValidFolder_ClearsTheHintAgainAndRescans()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = missingFolder;
        Assert.True(vm.HasPluginPathHint);

        vm.PluginPath = typedFolder;
        Assert.False(vm.HasPluginPathHint);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);

        Assert.Equal([typedFolder], scans);
    }

    [Fact]
    public void Cancel_RestoresTheStoredFolderAndRescansItWhenTheRuntimeShowsAPreview()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        vm.RescanPluginPathCommand.Execute(null);
        vm.MarkPluginPathScanned(typedFolder);

        vm.RevertPluginPathPreview();

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.Equal([typedFolder, storedFolder], scans);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 2);

        Assert.Equal(2, scans.Count);
    }

    [Fact]
    public void Cancel_WithoutAPreviewScan_OnlyPutsTheFieldBack()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;

        vm.RevertPluginPathPreview();

        Assert.Equal(storedFolder, vm.PluginPath);
        Assert.Empty(scans);
        Assert.False(vm.HasPluginPathHint);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 2);

        Assert.Empty(scans);
    }

    private static OptionsDialogViewModel CreateViewModel(out ManualTimeProvider clock)
    {
        clock = new ManualTimeProvider();
        return new OptionsDialogViewModel(["English"], clock);
    }

    private static List<string> RecordScans(OptionsDialogViewModel vm)
    {
        var scans = new List<string>();
        vm.PluginPathScanRequested += (_, _) => scans.Add(vm.PluginPath);
        return scans;
    }
}
