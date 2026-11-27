using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Capture;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class AddToListCaptureSourceTests
{
    private sealed class FakeDriveProvider : IMediaDriveProvider
    {
        public event EventHandler? DrivesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => [];
        public MediaDriveInfo? GetDrive(string drivePathOrName) => null;
    }

    private sealed class FakeCaptureProvider(IReadOnlyList<CaptureDevice>? devices = null)
        : ICaptureSourcePluginCapability
    {
        public CaptureSourceDraft? LastDraft { get; private set; }

        public string ProviderId => "fake-capture";

        public string DisplayName => "Fake capture";

        public IReadOnlyList<CaptureSourceKind> GetSourceKinds() =>
        [
            new("file", "File", "Capture file", UseFilePicker: true),
            new("camera", "Local device", "Capture device", UseDevicePicker: true),
            new("mjpeg", "MJPEG URL", "MJPEG stream URL")
        ];

        public IReadOnlyList<CaptureDevice> GetDevices(string kindId) => devices ?? [];

        public Task<CaptureSourceProbeResult> ProbeAsync(CaptureSourceConfiguration configuration,
            CancellationToken cancellationToken = default) => Task.FromResult(CaptureSourceProbeResult.Available());

        public Task<CapturePreviewResult> CapturePreviewAsync(CaptureSourceConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CapturePreviewResult.Failed("Camera is busy."));

        public IReadOnlyList<CaptureSourceConfiguration> GetSources() => [];

        public Task<CaptureSourceConfiguration> AddSourceAsync(CaptureSourceDraft draft,
            CancellationToken cancellationToken = default)
        {
            LastDraft = draft;
            return Task.FromResult(new CaptureSourceConfiguration("source-1", draft.KindId, draft.Location,
                draft.DestinationFolder, draft.CatalogFolder, draft.Label));
        }

        public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public IReadOnlyList<CaptureDetectorDescriptor> GetDetectors() => [];

        public CaptureDetectorSettings GetDetectorSettings() =>
            new(null, new Dictionary<string, double>());

        public Task SaveDetectorSettingsAsync(CaptureDetectorSettings settings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public event EventHandler? SourcesChanged
        {
            add { }
            remove { }
        }
    }

    private static string Destination => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skycd-captures"));

    private static AddToListDialogViewModel CreateViewModel(FakeCaptureProvider provider) =>
        new(new FakeDriveProvider(), captureProviders: [provider]);

    private static AddToListDialogViewModel CreateUrlViewModel(FakeCaptureProvider provider,
        string location = "http://example.com/stream.mjpg", string? destination = null)
    {
        var vm = CreateViewModel(provider);
        vm.SourceMode = AddToListSourceMode.CaptureSource;
        vm.SelectedCaptureKind = vm.CaptureKinds.First(kind => kind.Id == "mjpeg");
        vm.CaptureDestination = destination ?? Destination;
        vm.CaptureLocation = location;
        return vm;
    }

    [Fact]
    public void CaptureSource_RequiresPhysicalDestination()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider(), destination: "  ");

        Assert.False(vm.CanConfirm);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.CaptureDestinationIsRequired), vm.ValidationMessage);
    }

    [Fact]
    public void CaptureSource_RejectsRelativeDestination()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider(), destination: "captures");

        Assert.False(vm.CanConfirm);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.CaptureDestinationNotAbsolute), vm.ValidationMessage);
        Assert.Equal(vm.ValidationMessage, vm.CaptureDestinationValidationMessage);
        Assert.Null(vm.CaptureLocationValidationMessage);
    }

    [Fact]
    public void CaptureSource_RequiresLocation()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider(), location: "  ");

        Assert.False(vm.CanConfirm);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.CaptureSourceLocationRequired), vm.ValidationMessage);
        Assert.Equal(vm.ValidationMessage, vm.CaptureLocationValidationMessage);
        Assert.Null(vm.CaptureDestinationValidationMessage);
    }

    [Fact]
    public void UrlKind_RejectsAnInvalidUrl()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider(), location: "not a stream");

        Assert.False(vm.CanConfirm);
        Assert.Equal(LocalizationService.Current.Get(TextKeys.CaptureSourceUrlInvalid), vm.ValidationMessage);
        Assert.Equal(vm.ValidationMessage, vm.CaptureLocationValidationMessage);
    }

    [Fact]
    public void UrlKind_AcceptsAnAbsoluteUrl()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider());

        Assert.Null(vm.ValidationMessage);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void DeviceKind_FillsThePickerAndDefaultsTheSelection()
    {
        var provider = new FakeCaptureProvider([new CaptureDevice("cam-0", "Integrated camera")]);
        using var vm = CreateViewModel(provider);
        vm.SourceMode = AddToListSourceMode.CaptureSource;

        vm.SelectedCaptureKind = vm.CaptureKinds.First(kind => kind.Id == "camera");

        Assert.True(vm.ShowCaptureDevicePicker);
        Assert.False(vm.ShowCaptureLocationField);
        Assert.Single(vm.CaptureDevices);
        Assert.Equal("cam-0", vm.SelectedCaptureDevice?.Id);
        Assert.Equal("cam-0", vm.ResolveCaptureLocation());
    }

    [Fact]
    public void DeviceKind_WithoutDevices_ShowsAnInlineMessage()
    {
        using var vm = CreateViewModel(new FakeCaptureProvider());

        vm.SourceMode = AddToListSourceMode.CaptureSource;
        vm.SelectedCaptureKind = vm.CaptureKinds.First(kind => kind.Id == "camera");

        Assert.Empty(vm.CaptureDevices);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.CaptureNoDevices), vm.CaptureMessage);
    }

    [Fact]
    public void FileKind_UsesTheTrimmedPathAsLocation()
    {
        using var vm = CreateViewModel(new FakeCaptureProvider());
        vm.SourceMode = AddToListSourceMode.CaptureSource;
        vm.SelectedCaptureKind = vm.CaptureKinds.First(kind => kind.Id == "file");

        vm.CaptureLocation = "  C:\\frames\\0001.jpg  ";

        Assert.True(vm.ShowCaptureFilePicker);
        Assert.Equal("C:\\frames\\0001.jpg", vm.ResolveCaptureLocation());
    }

    [Fact]
    public void CreateCaptureDraft_FallsBackToTheKindNameAndLeavesCatalogFolderToTheHost()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider());

        var draft = vm.CreateCaptureDraft();

        Assert.Equal("mjpeg", draft.KindId);
        Assert.Equal("http://example.com/stream.mjpg", draft.Location);
        Assert.Equal(Destination, draft.DestinationFolder);
        Assert.Equal(string.Empty, draft.CatalogFolder);
        Assert.Equal("MJPEG URL", draft.Label);
    }

    [Fact]
    public async Task AddCaptureSourceAsync_DelegatesTheDraftToTheProvider()
    {
        var provider = new FakeCaptureProvider();
        using var vm = CreateUrlViewModel(provider);
        vm.CaptureLabel = "Front door";

        var configuration = await vm.AddCaptureSourceAsync();

        Assert.Equal("source-1", configuration.SourceId);
        Assert.NotNull(provider.LastDraft);
        Assert.Equal("Front door", provider.LastDraft!.Label);
        Assert.Equal(Destination, provider.LastDraft.DestinationFolder);
    }

    [Fact]
    public async Task CapturePreviewAsync_ReportsFailuresInline()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider());

        var result = await vm.CapturePreviewAsync();

        Assert.False(result?.Succeeded ?? true);
        Assert.Equal("Camera is busy.", vm.CaptureMessage);
        Assert.False(vm.IsCapturePreviewRunning);
    }

    [Fact]
    public void SelectSource_RejectsCaptureWithoutProviders()
    {
        using var vm = new AddToListDialogViewModel(new FakeDriveProvider());

        vm.SelectSourceCommand.Execute("CaptureSource");

        Assert.False(vm.HasCaptureProviders);
        Assert.NotEqual(AddToListSourceMode.CaptureSource, vm.SourceMode);
    }

    [Fact]
    public void CaptureMode_HidesTheNameAndDestinationRows()
    {
        using var vm = CreateUrlViewModel(new FakeCaptureProvider());

        Assert.True(vm.IsSourceCapture);
        Assert.False(vm.IsNameEntryVisible);
        Assert.False(vm.IsDestinationSectionVisible);
    }
}
