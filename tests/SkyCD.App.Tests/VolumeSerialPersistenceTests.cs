using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.Staging;
using SkyCD.Plugin.Host.Media;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class VolumeSerialPersistenceTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();
    private readonly TestWorkspace workspace = new();
    private readonly CultureInfo originalServiceCulture = LocalizationService.Current.CurrentCulture;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

    private string scanTestDirectory => workspace.Root;

    public void Dispose()
    {
        LocalizationService.Current.SetCulture(originalServiceCulture);
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUiCulture;
        sandbox.Dispose();
        workspace.Dispose();
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

    private static readonly MediaMetadataSchema FakeSchema = new("fake", 1, []);

    private sealed class FixedVolumeProvider(string serial) : IMediaTargetMetadataProvider
    {
        public bool HasProviders => true;

        public Task<IReadOnlyList<MediaTargetProviderCapture>> CaptureAsync(MediaTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaTargetProviderCapture>>(
            [
                new("fake", "1", "fake", new MediaTargetCapture(FakeSchema,
                    [new("VolumeSerialNumber", MediaMetadataType.Text, Text: serial)]))
            ]);
    }

    private sealed class MissingVolumeProvider : IMediaTargetMetadataProvider
    {
        public bool HasProviders => true;

        public Task<IReadOnlyList<MediaTargetProviderCapture>> CaptureAsync(MediaTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MediaTargetProviderCapture>>([]);
    }

    private sealed class TestStack : IDisposable
    {
        public TestStack()
        {
            Provider = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(Provider);
            Manager = Provider.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)Manager.For<CatalogDocument>();
            foreach (var entry in Catalog.CreateDefaultEntries())
            {
                Catalog.Save(entry.Id, entry);
            }
        }

        public Container Provider { get; }
        public RepositoryManager Manager { get; }
        public CatalogDocumentRepository Catalog { get; }

        public void Dispose() => Provider.Dispose();
    }

    private IReadOnlyList<CatalogDocument> ScanMedia(IMediaTargetMetadataProvider? mediaTargets)
    {
        var cdRoot = Path.Combine(scanTestDirectory, "DiscRoot");
        Directory.CreateDirectory(cdRoot);
        File.WriteAllText(Path.Combine(cdRoot, "track01.cda"), "audio-data");

        var drive = new MediaDriveInfo(
            Name: "D:\\",
            VolumeLabel: "GameDisc",
            RootDirectory: cdRoot,
            DriveType: DriveType.CDRom,
            IsReady: true,
            AvailableFreeSpace: 0,
            TotalFreeSpace: 0,
            TotalSize: 700_000_000,
            DriveFormat: "CDFS");

        var scanner = new MediaCatalogScanner(new FakeDriveProvider([drive]), mediaTargets: mediaTargets);
        return scanner.Scan(new MediaScanOptions(
            DrivePathOrName: "D:\\",
            MediaName: "GameDisc",
            TargetParentId: null,
            RootType: CatalogDocumentType.Media,
            IncludeMediaInfo: true));
    }

    [Fact]
    public async Task CapturedSerial_PersistsAcrossReopen_AndShowsLocalizedInProperties()
    {
        using var stack = new TestStack();
        using var viewModel = new MainWindowViewModel(stack.Manager);
        await viewModel.AddImportedDocumentsAsync(ScanMedia(new FixedVolumeProvider("1234-ABCD")));

        // A reopened catalog holds the stored property with no volume or plugin involvement.
        using var reloadedVm = new MainWindowViewModel(stack.Manager);
        var mediaDoc = Assert.Single(stack.Catalog.GetAll(),
            doc => doc.Name == "GameDisc" && doc.Type == CatalogDocumentType.Media);
        Assert.Equal("1234-ABCD", mediaDoc.Properties["VolumeSerialNumber"]?.ToString());

        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("en"));
        var englishDialog = new PropertiesDialogViewModel(mediaDoc.Id, mediaDoc.Name, "icon", string.Empty, mediaDoc.Properties);
        Assert.True(englishDialog.InfoProperties.ContainsKey("Volume serial number"));
        Assert.Equal("1234-ABCD", englishDialog.InfoProperties["Volume serial number"]?.ToString());

        LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo("lt"));
        var lithuanianDialog = new PropertiesDialogViewModel(mediaDoc.Id, mediaDoc.Name, "icon", string.Empty, mediaDoc.Properties);
        Assert.True(lithuanianDialog.InfoProperties.ContainsKey("Tomo serijos numeris"));
        Assert.Equal("1234-ABCD", lithuanianDialog.InfoProperties["Tomo serijos numeris"]?.ToString());
    }

    [Fact]
    public async Task RefreshWithoutCapture_NeverErasesExistingSerial()
    {
        using var stack = new TestStack();
        using var viewModel = new MainWindowViewModel(stack.Manager);
        await viewModel.AddImportedDocumentsAsync(ScanMedia(new FixedVolumeProvider("1234-ABCD")));

        // A later background refresh whose provider can read nothing must not erase the captured serial.
        var rescanned = ScanMedia(new MissingVolumeProvider());
        var mediaRoot = Assert.Single(rescanned, doc => doc.Type == CatalogDocumentType.Media);
        Assert.False(mediaRoot.Properties.ContainsKey("VolumeSerialNumber"));

        new CatalogScanPublisher().PublishDirect(rescanned, stack.Catalog);

        var reloaded = Assert.Single(stack.Catalog.GetAll(),
            doc => doc.Name == "GameDisc" && doc.Type == CatalogDocumentType.Media);
        Assert.Equal("1234-ABCD", reloaded.Properties["VolumeSerialNumber"]?.ToString());
    }
}
