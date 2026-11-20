using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Documents;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class ScanCompletionSettingsTests
{
    private const string DrivePath = @"E:\";

    [Fact]
    public void Default_DisablesEveryCompletionAction()
    {
        Assert.False(ScanCompletionSettings.Default.EjectMediaAfterScan);
        Assert.False(ScanCompletionSettings.Default.ActivateMainWindowAfterScan);
        Assert.False(ScanCompletionSettings.Default.PlaySoundAfterScan);
        Assert.False(ScanCompletionSettings.Default.HasAnyAction);
    }

    [Fact]
    public void FromOptions_MapsPersistedSettings()
    {
        var options = new AppOptionsDocument
        {
            EjectMediaAfterScan = true,
            ActivateMainWindowAfterScan = false,
            PlaySoundAfterScan = true
        };

        var settings = ScanCompletionSettings.FromOptions(options);

        Assert.True(settings.EjectMediaAfterScan);
        Assert.False(settings.ActivateMainWindowAfterScan);
        Assert.True(settings.PlaySoundAfterScan);
        Assert.True(settings.HasAnyAction);
    }

    [Fact]
    public void Complete_WithAllActionsDisabled_DoesNothing()
    {
        var ejector = new FakeMediaEjector();
        var presenter = new FakeNotificationPresenter();

        var result = CreateService(ejector, presenter).Complete(ScanCompletionSettings.Default, DrivePath);

        Assert.Equal(MediaEjectionOutcome.NotApplicable, result.MediaEjection);
        Assert.False(result.MainWindowActivated);
        Assert.False(result.SoundPlayed);
        Assert.Empty(ejector.EjectedSources);
        Assert.Empty(presenter.Calls);
    }

    [Fact]
    public void Complete_WithAllActionsEnabled_EjectsActivatesAndPlaysSound()
    {
        var ejector = new FakeMediaEjector();
        var presenter = new FakeNotificationPresenter();
        var settings = new ScanCompletionSettings(true, true, true);

        var result = CreateService(ejector, presenter).Complete(settings, DrivePath);

        Assert.Equal(MediaEjectionOutcome.Ejected, result.MediaEjection);
        Assert.True(result.MainWindowActivated);
        Assert.True(result.SoundPlayed);
        Assert.Equal([DrivePath], ejector.EjectedSources);
        Assert.Equal(["activate", "sound"], presenter.Calls);
    }

    [Fact]
    public void Complete_WithoutMediaSource_SkipsEjection()
    {
        var ejector = new FakeMediaEjector();

        var result = CreateService(ejector, new FakeNotificationPresenter())
            .Complete(new ScanCompletionSettings(true, false, false));

        Assert.Equal(MediaEjectionOutcome.NotApplicable, result.MediaEjection);
        Assert.Empty(ejector.EjectedSources);
    }

    [Fact]
    public void Complete_WhenEjectorIsUnsupported_StillRunsOtherActions()
    {
        var ejector = new FakeMediaEjector { Outcome = MediaEjectionOutcome.PlatformUnsupported };
        var presenter = new FakeNotificationPresenter();

        var result = CreateService(ejector, presenter)
            .Complete(new ScanCompletionSettings(true, true, true), DrivePath);

        Assert.Equal(MediaEjectionOutcome.PlatformUnsupported, result.MediaEjection);
        Assert.True(result.MainWindowActivated);
        Assert.True(result.SoundPlayed);
    }

    [Fact]
    public void Complete_WhenEjectorThrows_SwallowsFailure()
    {
        var ejector = new FakeMediaEjector { ThrowOnEject = true };
        var presenter = new FakeNotificationPresenter();

        var result = CreateService(ejector, presenter)
            .Complete(new ScanCompletionSettings(true, true, true), DrivePath);

        Assert.Equal(MediaEjectionOutcome.Failed, result.MediaEjection);
        Assert.True(result.MainWindowActivated);
        Assert.True(result.SoundPlayed);
    }

    [Fact]
    public void Complete_WhenPresenterThrows_ReportsActionAsNotPerformed()
    {
        var presenter = new FakeNotificationPresenter { ThrowOnActivate = true, ThrowOnSound = true };

        var result = CreateService(new FakeMediaEjector(), presenter)
            .Complete(new ScanCompletionSettings(false, true, true));

        Assert.False(result.MainWindowActivated);
        Assert.False(result.SoundPlayed);
    }

    [Fact]
    public void OptionsDialogViewModel_ScanCompletionSettingsRoundTrip()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetScanOptions(new ScanSettingsSnapshot(
            SkipSymbolicLinks: false,
            HiddenSystemFilePolicy: "Include",
            SkipRecycleBins: true,
            SkipWellKnownSystemFolders: true,
            SkipEmptyFolders: false,
            SkipOnlineOnlyCloudFiles: false,
            ShowScanErrorsAfterScan: true,
            SaveScanErrorsToComments: false,
            EjectMediaAfterScan: true,
            ActivateMainWindowAfterScan: false,
            PlaySoundAfterScan: true));

        var scanOptions = vm.GetScanOptions();

        Assert.True(scanOptions.EjectMediaAfterScan);
        Assert.False(scanOptions.ActivateMainWindowAfterScan);
        Assert.True(scanOptions.PlaySoundAfterScan);
    }

    [Fact]
    public void OptionsDialogViewModel_UnsupportedEjectionDisablesOptionAndShowsHint()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        Assert.True(vm.IsMediaEjectionSupported);

        vm.SetMediaEjectionSupported(false);

        Assert.False(vm.IsMediaEjectionSupported);
        Assert.NotEqual(
            new OptionsDialogViewModel(["English"]).MediaEjectionSupportHint,
            vm.MediaEjectionSupportHint);
    }

    private static ScanCompletionService CreateService(IMediaEjector ejector, IUserNotificationPresenter presenter) =>
        new(ejector, presenter, NullLogger<ScanCompletionService>.Instance);

    private sealed class FakeMediaEjector : IMediaEjector
    {
        public List<string?> EjectedSources { get; } = [];

        public bool IsPlatformSupported { get; init; } = true;

        public MediaEjectionOutcome Outcome { get; init; } = MediaEjectionOutcome.Ejected;

        public bool ThrowOnEject { get; init; }

        public MediaEjectionSupport GetSupport(string? source) => MediaEjectionSupport.Supported;

        public MediaEjectionOutcome Eject(string? source)
        {
            if (ThrowOnEject)
            {
                throw new InvalidOperationException("The drive refused the eject request.");
            }

            EjectedSources.Add(source);
            return Outcome;
        }
    }

    private sealed class FakeNotificationPresenter : IUserNotificationPresenter
    {
        public List<string> Calls { get; } = [];

        public bool ThrowOnActivate { get; init; }

        public bool ThrowOnSound { get; init; }

        public void ActivateMainWindow()
        {
            if (ThrowOnActivate)
            {
                throw new InvalidOperationException("Window activation failed.");
            }

            Calls.Add("activate");
        }

        public void PlayScanCompleteSound()
        {
            if (ThrowOnSound)
            {
                throw new InvalidOperationException("Sound playback failed.");
            }

            Calls.Add("sound");
        }
    }
}
