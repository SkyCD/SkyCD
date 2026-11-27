using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
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

    [Fact]
    public void EnterOrLostFocus_RescansEvenWhenTheFolderIsAlreadyTheScannedOne()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        vm.RescanPluginPathCommand.Execute(null);
        vm.MarkPluginPathScanned(typedFolder);

        vm.RescanPluginPathCommand.Execute(null);

        Assert.Equal([typedFolder, typedFolder], scans);

        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay * 2);

        Assert.Equal(2, scans.Count);
    }

    [Fact]
    public void AfterAScan_ANewerFolderStillRequestsItsOwnScan()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        vm.RescanPluginPathCommand.Execute(null);
        vm.MarkPluginPathScanned(typedFolder);

        vm.PluginPath = otherFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);

        Assert.Equal([typedFolder, otherFolder], scans);
    }

    [Fact]
    public void SupersededScan_AcknowledgedForTheScannedFolder_KeepsTheNewerFolderPending()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);
        Assert.Equal([typedFolder], scans);

        // The user typed on while the typed-folder scan was still running; the host acknowledges the
        // scan for the folder it actually enumerated, not for the live field text.
        vm.PluginPath = otherFolder;
        vm.MarkPluginPathScanned(typedFolder);
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);

        Assert.Equal([typedFolder, otherFolder], scans);
    }

    [Fact]
    public void RetracingTheFolderPath_RescansTheEarlierFolderAgain()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);
        vm.MarkPluginPathScanned(typedFolder);
        vm.PluginPath = otherFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);
        vm.MarkPluginPathScanned(otherFolder);

        vm.PluginPath = typedFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);

        Assert.Equal([typedFolder, otherFolder, typedFolder], scans);
    }

    [Fact]
    public void TypingBackToTheScannedFolder_DoesNotRequestAScan()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);
        vm.PluginPath = typedFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);
        vm.MarkPluginPathScanned(typedFolder);

        vm.PluginPath = storedFolder;
        vm.PluginPath = typedFolder;
        clock.Advance(OptionsDialogViewModel.PluginPathScanDelay);

        Assert.Equal([typedFolder], scans);
    }

    [Fact]
    public void ExplicitRescan_OfAFolderThatDoesNotExist_StaysGated()
    {
        using var vm = CreateViewModel(out var clock);
        var scans = RecordScans(vm);
        vm.InitializePluginPath(storedFolder);

        vm.PluginPath = missingFolder;
        vm.RescanPluginPathCommand.Execute(null);

        Assert.Empty(scans);
        Assert.True(vm.HasPluginPathHint);
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
        vm.PluginPathScanRequested += (_, e) => scans.Add(e.Path);
        return scans;
    }
}
