using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.AiDescription;
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
    public async Task FolderScan_CollectsMetadataBeforePublication_AndRescanReusesCapturedData()
    {
        File.WriteAllText(Path.Combine(scanTestDirectory, "hash.txt"), "hashable content");
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        foreach (var entry in catalog.CreateDefaultEntries()) catalog.Save(entry.Id, entry);
        var optionsRepository = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();
        var options = optionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 10;
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);
        using var viewModel = new MainWindowViewModel(repositories);
        var capability = new CountingMetadataCapability();
        viewModel.RefreshMediaServices(new MediaExtensionManager([new MediaProvider("hash", "1", capability)]));
        var scanner = new FolderCatalogScanner();
        var scanOptions = new FolderScanOptions(scanTestDirectory, TargetParentId: "library");
        var firstScan = scanner.Scan(scanOptions);
        var firstFile = Assert.Single(firstScan, entry => entry.Type == CatalogDocumentType.File);

        var adding = viewModel.AddImportedDocumentsAsync(firstScan);
        await capability.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(adding.IsCompleted);
        Assert.Null(catalog.Get(firstFile.Id));
        Assert.Empty(viewModel.MediaCache.GetRecordsForEntry(firstFile.Id));
        capability.Release();
        await adding.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(catalog.Get(firstFile.Id));
        Assert.Equal(1, capability.Calls);
        Assert.Equal(MediaCacheRecordState.Captured, Assert.Single(viewModel.MediaCache.GetRecordsForEntry(firstFile.Id)).State);

        var secondScan = scanner.Scan(scanOptions);
        var secondFile = Assert.Single(secondScan, entry => entry.Type == CatalogDocumentType.File);
        Assert.NotEqual(firstFile.Id, secondFile.Id);
        await viewModel.AddImportedDocumentsAsync(secondScan);

        Assert.Equal(1, capability.Calls);
        Assert.Equal(MediaCacheRecordState.Captured, Assert.Single(viewModel.MediaCache.GetRecordsForEntry(firstFile.Id)).State);
        Assert.Empty(viewModel.MediaCache.GetRecordsForEntry(secondFile.Id));
        Assert.NotNull(catalog.Get(firstFile.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddingFolderDescribesEachOriginalFileWithoutCachedMetadataWhenProviderIsEnabled(bool enabled)
    {
        File.WriteAllText(Path.Combine(scanTestDirectory, "first.txt"), "first test file");
        File.WriteAllText(Path.Combine(scanTestDirectory, "second.txt"), "second test file");
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        foreach (var entry in catalog.CreateDefaultEntries()) catalog.Save(entry.Id, entry);
        using var viewModel = new MainWindowViewModel(repositories);
        var config = Path.Combine(appDataRoot, "test-agent.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new DescriptionAgentOptions
        {
            Enabled = enabled,
            Executable = Environment.ProcessPath!
        }));
        var contexts = new ConcurrentBag<DescriptionContext>();
        var capability = new AiDescriptionPlugin(config, (_, context, _) =>
        {
            contexts.Add(context);
            if (context.OriginalFile?.Name == "second.txt")
                throw new DescriptionAgentException("test agent failed");
            return Task.FromResult(new GeneratedDescription("A test description", "test agent", null, "1", "1", "English",
                "content-based AI description", "local-inference", DateTimeOffset.UtcNow));
        });
        viewModel.RefreshDescriptionServices(new DescriptionExtensionManager([capability]));
        var scanner = new FolderCatalogScanner();
        var options = new FolderScanOptions(scanTestDirectory, TargetParentId: "library");

        await viewModel.AddImportedDocumentsAsync(scanner.Scan(options));

        var files = catalog.GetAll().Where(entry => entry.Name is "first.txt" or "second.txt").ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal(enabled ? 2 : 0, contexts.Count);
        foreach (var context in contexts)
        {
            Assert.Empty(context.Parts);
            var original = Assert.IsType<DescriptionOriginalFile>(context.OriginalFile);
            Assert.Equal(Path.Combine(scanTestDirectory, original.Name), original.FullPath);
            Assert.Equal($"{Path.GetFileNameWithoutExtension(original.Name)} test file", File.ReadAllText(original.FullPath));
        }
        var descriptions = new AiDescriptionService(catalog, viewModel.MediaCache);
        Assert.Equal(enabled ? "A test description" : null,
            descriptions.Read(files.Single(file => file.Name == "first.txt").Id).Text);
        Assert.DoesNotContain(viewModel.MediaCache.GetRecordsForEntry(files.Single(file => file.Name == "second.txt").Id),
            CatalogDescriptionService.IsGeneratedDescriptionRecord);

        await viewModel.AddImportedDocumentsAsync(scanner.Scan(options));

        Assert.Equal(enabled ? 2 : 0, contexts.Count);
    }

    private sealed class CountingMetadataCapability : IMediaMetadataCapability
    {
        private int calls;
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref calls);
        public void Release() => gate.TrySetResult();
        public MediaDescriptor Descriptor { get; } = new(["*"], ["*"],
            new MediaMetadataSchema("test.hash", 1, [new MediaMetadataField("hash", MediaMetadataType.Text)]),
            Priority: 0, IsOptIn: true);

        public async Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Started.TrySetResult();
            await gate.Task.WaitAsync(cancellationToken);
            await source.ReadAsync(0, (int)Math.Min(item.Length, 1), cancellationToken);
            return new MediaExtractionResult([new MediaMetadataValue("hash", MediaMetadataType.Text, Text: "captured")]);
        }
    }

    [Fact]
    public async Task FolderScan_And_AddImportedDocuments_PersistsToCouchbase_AndUpdatesUI()
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
        await viewModel.AddImportedDocumentsAsync(scannedDocs);

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

    [Fact]
    public async Task FolderScan_WithNewMediaPlacement_And_WithoutSubfolders_ImportsCorrectly()
    {
        var mediaDir = Path.Combine(scanTestDirectory, "BackupFolder");
        Directory.CreateDirectory(mediaDir);
        File.WriteAllText(Path.Combine(mediaDir, "root_file.txt"), "root-data");

        var ignoredSubDir = Path.Combine(mediaDir, "IgnoredSubDir");
        Directory.CreateDirectory(ignoredSubDir);
        File.WriteAllText(Path.Combine(ignoredSubDir, "child.txt"), "child-data");

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

        var scanner = new FolderCatalogScanner();
        var scanOptions = new FolderScanOptions(
            FolderPath: mediaDir,
            MediaName: "Custom Media Title",
            TargetParentId: null,
            RootType: CatalogDocumentType.Media,
            IncludeSubfolders: false,
            IncludeExtendedInfo: true);

        var scannedDocs = scanner.Scan(scanOptions);
        Assert.Equal(2, scannedDocs.Count); // Media root + root_file.txt

        await viewModel.AddImportedDocumentsAsync(scannedDocs);

        var rootDoc = scannedDocs[0];
        Assert.Equal("Custom Media Title", rootDoc.Name);
        Assert.Equal(CatalogDocumentType.Media, rootDoc.Type);
        Assert.Null(rootDoc.ParentId);

        using var reloadedVm = new MainWindowViewModel(repositoryManager);
        var rootMediaNode = reloadedVm.TreeNodes.FirstOrDefault(node => node.Title == "Custom Media Title");
        Assert.NotNull(rootMediaNode);

        reloadedVm.SelectedTreeNode = rootMediaNode;
        Assert.Single(reloadedVm.BrowserItems);
        Assert.Contains(reloadedVm.BrowserItems, item => item.Name == "root_file.txt");
        Assert.DoesNotContain(reloadedVm.BrowserItems, item => item.Name == "IgnoredSubDir");
    }
}
