using System;
using System.IO;
using System.Linq;
using Couchbase.Lite;
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

public sealed class FolderScanningIntegrationTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-scan-cblite-{Guid.NewGuid():N}");
    private readonly string scanTestDirectory = Path.Combine(Path.GetTempPath(), $"skycd-scan-source-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public FolderScanningIntegrationTests()
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

    [Fact]
    public void FolderScan_And_AddImportedDocuments_PersistsToCouchbase_AndUpdatesUI()
    {
        // 1. Create directory structure on disk
        var musicDir = Path.Combine(scanTestDirectory, "MusicAlbum");
        Directory.CreateDirectory(musicDir);
        File.WriteAllText(Path.Combine(musicDir, "track1.mp3"), "audio-data-1");
        File.WriteAllText(Path.Combine(musicDir, "track2.mp3"), "audio-data-22");

        var bonusDir = Path.Combine(musicDir, "Bonus");
        Directory.CreateDirectory(bonusDir);
        File.WriteAllText(Path.Combine(bonusDir, "bonus.mp3"), "bonus-audio");

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
        options.AppStartCount = 10;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);

        using var viewModel = new MainWindowViewModel(repositoryManager);
        var initialItemCount = viewModel.BrowserItems.Count;
        Assert.False(viewModel.IsDirtyDocument);

        // 3. Scan folder
        var scanner = new FolderCatalogScanner();
        var scanOptions = new FolderScanOptions(
            FolderPath: musicDir,
            MediaName: "MusicAlbum",
            TargetParentId: "library",
            RootType: CatalogDocumentType.Folder,
            IncludeSubfolders: true,
            IncludeExtendedInfo: true);

        var scannedDocs = scanner.Scan(scanOptions);
        Assert.Equal(5, scannedDocs.Count); // Root + 2 tracks + Bonus + bonus track

        // 4. Import into ViewModel
        viewModel.AddImportedDocuments(scannedDocs);

        Assert.True(viewModel.IsDirtyDocument);
        Assert.Equal(initialItemCount + 1, viewModel.BrowserItems.Count);
        Assert.Contains(viewModel.BrowserItems, item => item.Name == "MusicAlbum");

        // Verify in repository
        var allDocs = catalogRepository.GetAll();
        Assert.Contains(allDocs, doc => doc.Name == "MusicAlbum" && doc.Type == CatalogDocumentType.Folder);
        Assert.Contains(allDocs, doc => doc.Name == "track1.mp3" && doc.Type == CatalogDocumentType.File);
        Assert.Contains(allDocs, doc => doc.Name == "Bonus" && doc.Type == CatalogDocumentType.Folder);
        Assert.Contains(allDocs, doc => doc.Name == "bonus.mp3" && doc.Type == CatalogDocumentType.File);

        // 5. Verify reloading in a new MainWindowViewModel
        using var reloadedVm = new MainWindowViewModel(repositoryManager);
        var libraryNode = reloadedVm.TreeNodes.Single(node => node.Key == "library");
        reloadedVm.SelectedTreeNode = libraryNode;

        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "MusicAlbum");

        var musicAlbumNode = libraryNode.Children.FirstOrDefault(node => node.Title == "MusicAlbum");
        Assert.NotNull(musicAlbumNode);

        reloadedVm.SelectedTreeNode = musicAlbumNode;
        Assert.Equal(3, reloadedVm.BrowserItems.Count); // track1, track2, Bonus
        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "track1.mp3");
        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "Bonus");
    }
}
