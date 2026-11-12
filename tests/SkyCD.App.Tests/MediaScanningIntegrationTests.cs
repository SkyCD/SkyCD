using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class MediaScanningIntegrationTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-media-cblite-{Guid.NewGuid():N}");
    private readonly string scanTestDirectory = Path.Combine(Path.GetTempPath(), $"skycd-media-source-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public MediaScanningIntegrationTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(scanTestDirectory);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        try
        {
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
        catch { }

        try
        {
            if (Directory.Exists(scanTestDirectory))
            {
                Directory.Delete(scanTestDirectory, recursive: true);
            }
        }
        catch { }
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
    public void MediaScan_And_AddImportedDocuments_PersistsToCouchbase_AndUpdatesUI_AndReloadsCorrectly()
    {
        // 1. Create media directory structure on disk
        var cdRoot = Path.Combine(scanTestDirectory, "CDDriveRoot");
        Directory.CreateDirectory(cdRoot);
        File.WriteAllText(Path.Combine(cdRoot, "AUTORUN.INF"), "[autorun]\nopen=setup.exe");
        File.WriteAllText(Path.Combine(cdRoot, "setup.exe"), "binary-setup-data");

        var docsDir = Path.Combine(cdRoot, "Manual");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(Path.Combine(docsDir, "guide.pdf"), "pdf-content-here");

        // 2. Initialize Container and Repositories
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositoryManager = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();

        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 1;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var initialItemCount = viewModel.BrowserItems.Count;
        Assert.False(viewModel.IsDirtyDocument);

        // 3. Setup fake drive provider and scan media
        var fakeDrive = new MediaDriveInfo(
            Name: "D:\\",
            VolumeLabel: "GameDisc_v1",
            RootDirectory: cdRoot,
            DriveType: DriveType.CDRom,
            IsReady: true,
            AvailableFreeSpace: 0,
            TotalFreeSpace: 0,
            TotalSize: 700_000_000,
            DriveFormat: "CDFS");

        var driveProvider = new FakeDriveProvider([fakeDrive]);
        var scanner = new MediaCatalogScanner(driveProvider);
        var scanOptions = new MediaScanOptions(
            DrivePathOrName: "D:\\",
            MediaName: "GameDisc_v1",
            TargetParentId: null,
            RootType: CatalogDocumentType.Media,
            IncludeMediaInfo: true,
            IncludeExtendedInfo: true,
            IncludeSubfolders: true);

        var scannedDocs = scanner.Scan(scanOptions);
        Assert.Equal(5, scannedDocs.Count); // Root Media + AUTORUN.INF + setup.exe + Manual + guide.pdf

        // 4. Import into ViewModel
        viewModel.AddImportedDocuments(scannedDocs);

        Assert.True(viewModel.IsDirtyDocument);

        // Verify root media document in repository
        var allDocs = catalogRepository.GetAll();
        var mediaDoc = Assert.Single(allDocs, doc => doc.Name == "GameDisc_v1" && doc.Type == CatalogDocumentType.Media);
        Assert.Null(mediaDoc.ParentId);
        Assert.Equal("GameDisc_v1", mediaDoc.Properties["VolumeLabel"]?.ToString());
        Assert.Equal("700000000", mediaDoc.Properties["TotalSize"]?.ToString());
        Assert.Equal("CDFS", mediaDoc.Properties["DriveFormat"]?.ToString());

        Assert.Contains(allDocs, doc => doc.Name == "AUTORUN.INF" && doc.Type == CatalogDocumentType.File);
        Assert.Contains(allDocs, doc => doc.Name == "Manual" && doc.Type == CatalogDocumentType.Folder);
        Assert.Contains(allDocs, doc => doc.Name == "guide.pdf" && doc.Type == CatalogDocumentType.File);

        // 5. Verify reloading in a new MainWindowViewModel
        using var reloadedVm = new MainWindowViewModel(repositoryManager);
        var mediaTreeNode = reloadedVm.TreeNodes.FirstOrDefault(node => node.Title == "GameDisc_v1");
        Assert.NotNull(mediaTreeNode);

        reloadedVm.SelectedTreeNode = mediaTreeNode;
        Assert.Equal(3, reloadedVm.BrowserItems.Count); // AUTORUN.INF, setup.exe, Manual
        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "AUTORUN.INF");
        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "setup.exe");
        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "Manual");

        var manualTreeNode = mediaTreeNode.Children.FirstOrDefault(node => node.Title == "Manual");
        Assert.NotNull(manualTreeNode);

        reloadedVm.SelectedTreeNode = manualTreeNode;
        var manualGuideItem = Assert.Single(reloadedVm.BrowserItems);
        Assert.Equal("guide.pdf", manualGuideItem.Name);
    }
}
