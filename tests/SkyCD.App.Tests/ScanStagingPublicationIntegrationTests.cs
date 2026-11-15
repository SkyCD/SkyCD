using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Core.UndoRedo;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Staging;
using SkyCD.Presentation.Staging.Exceptions;
using SkyCD.Presentation.UndoRedo.Actions;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class ScanStagingPublicationIntegrationTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-staging-cblite-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public ScanStagingPublicationIntegrationTests()
    {
        Directory.CreateDirectory(appDataRoot);
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
    }

    private sealed class TestDatabaseContext(
        Container container,
        RepositoryManager repositoryManager,
        CatalogDocumentRepository catalogRepository) : IDisposable
    {
        public Container Container { get; } = container;
        public RepositoryManager RepositoryManager { get; } = repositoryManager;
        public CatalogDocumentRepository CatalogRepository { get; } = catalogRepository;

        public void Dispose()
        {
            Container.Dispose();
        }
    }

    private TestDatabaseContext InitializeRepositories()
    {
        var container = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(container);
        var repositoryManager = container.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositoryManager.For<CatalogDocument>();

        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 10;
        appOptionsRepository.Save(AppOptionsDocument.DocumentId, options);

        return new TestDatabaseContext(container, repositoryManager, catalogRepository);
    }

    [Fact]
    public void StagingStore_IsolatesStagedDocuments_UntilPublished()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        var initialCount = repository.GetAll().Count;

        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);
        var session = store.CreateSession(sourcePathOrAddress: @"C:\Data\Photos", targetParentId: "library", mediaName: "Photos");

        var doc1 = new CatalogDocument
        {
            Id = $"scan-root-{Guid.NewGuid():N}",
            Name = "Photos",
            ParentId = "library",
            Type = CatalogDocumentType.Folder
        };
        var doc2 = new CatalogDocument
        {
            Id = $"scan-file-{Guid.NewGuid():N}",
            Name = "img01.jpg",
            ParentId = doc1.Id,
            Type = CatalogDocumentType.File,
            Size = 1024
        };

        store.StageDocuments(session.SessionId, [doc1, doc2]);

        // Main repository is untouched
        Assert.Equal(initialCount, repository.GetAll().Count);
        Assert.Null(repository.Get(doc1.Id));
        Assert.Null(repository.Get(doc2.Id));

        // Staged store has documents
        var staged = store.GetStagedDocuments(session.SessionId);
        Assert.Equal(2, staged.Count);
        Assert.Contains(staged, d => d.Name == "Photos");
        Assert.Contains(staged, d => d.Name == "img01.jpg");
    }

    [Fact]
    public void CatalogScanPublisher_PublishesStagedScan_AtomicallyAndCleansUpSession()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);

        var publisher = new CatalogScanPublisher();
        var session = publisher.StartSession(store, @"C:\RockAlbums", "library", "RockAlbums");

        var rootDoc = new CatalogDocument
        {
            Id = $"rock-root-{Guid.NewGuid():N}",
            Name = "RockAlbums",
            ParentId = "library",
            Type = CatalogDocumentType.Folder
        };
        var trackDoc = new CatalogDocument
        {
            Id = $"track-{Guid.NewGuid():N}",
            Name = "track.mp3",
            ParentId = rootDoc.Id,
            Type = CatalogDocumentType.File,
            Size = 5000
        };

        publisher.StageDocuments(store, session.SessionId, [rootDoc, trackDoc]);

        var undoHistory = new UndoRedoHistory();
        var result = publisher.Publish(store, session.SessionId, repository, new ScanPublishOptions(), undoHistory);

        Assert.Equal(2, result.PublishedDocumentsCount);
        Assert.Equal(2, result.InsertedDocumentsCount);
        Assert.Equal(0, result.UpdatedDocumentsCount);
        Assert.False(result.IsIncomplete);

        // Verify documents are now in repository
        Assert.NotNull(repository.Get(rootDoc.Id));
        Assert.NotNull(repository.Get(trackDoc.Id));

        // Staging session is dropped
        Assert.Null(store.GetSession(session.SessionId));
        Assert.True(undoHistory.CanUndo);
    }

    [Fact]
    public void CatalogScanPublisher_CancelWithoutKeepingPartial_LeavesCatalogClean()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        var initialCount = repository.GetAll().Count;

        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);
        var publisher = new CatalogScanPublisher();
        var session = publisher.StartSession(store, @"C:\LargeScan", "library", "LargeScan");

        var doc1 = new CatalogDocument { Id = "staged-1", Name = "Doc1", ParentId = "library", Type = CatalogDocumentType.Folder };
        publisher.StageDocuments(store, session.SessionId, [doc1]);

        publisher.CancelSession(store, session.SessionId, keepPartial: false, repository);

        // Main repository untouched
        Assert.Equal(initialCount, repository.GetAll().Count);
        Assert.Null(repository.Get("staged-1"));
    }

    [Fact]
    public void CatalogScanPublisher_CancelWithKeepingPartial_PublishesWithIncompleteMarker()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);

        var publisher = new CatalogScanPublisher();
        var session = publisher.StartSession(store, @"C:\PartialScan", "library", "PartialScan");

        var doc1 = new CatalogDocument { Id = "partial-root", Name = "PartialScan", ParentId = "library", Type = CatalogDocumentType.Folder };
        var doc2 = new CatalogDocument { Id = "partial-file", Name = "file1.txt", ParentId = "partial-root", Type = CatalogDocumentType.File };
        publisher.StageDocuments(store, session.SessionId, [doc1, doc2]);

        var result = publisher.CancelSession(
            store,
            session.SessionId,
            keepPartial: true,
            repository,
            new ScanPublishOptions { ErrorSummary = "Cancelled by user" });

        Assert.NotNull(result);
        Assert.True(result.IsIncomplete);
        Assert.Equal(2, result.PublishedDocumentsCount);

        var savedRoot = repository.Get("partial-root");
        Assert.NotNull(savedRoot);
        Assert.Equal(CatalogContentsState.Incomplete, savedRoot.ContentsState);
        Assert.Equal("Cancelled by user", savedRoot.Properties["ScanErrorSummary"]?.ToString());
    }

    [Fact]
    public void CatalogScanPublisher_FailWithKeepingPartial_PublishesWithIncompleteAndErrorSummary()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);

        var publisher = new CatalogScanPublisher();
        var session = publisher.StartSession(store, "ftp://example.com/data", "library", "FtpData");

        var doc1 = new CatalogDocument { Id = "ftp-root", Name = "FtpData", ParentId = "library", Type = CatalogDocumentType.NetworkFolder };
        publisher.StageDocuments(store, session.SessionId, [doc1]);

        var result = publisher.FailSession(
            store,
            session.SessionId,
            errorSummary: "Connection timeout after 3 retries",
            keepPartial: true,
            repository);

        Assert.NotNull(result);
        Assert.True(result.IsIncomplete);
        Assert.Equal("Connection timeout after 3 retries", result.ErrorSummary);

        var savedRoot = repository.Get("ftp-root");
        Assert.NotNull(savedRoot);
        Assert.Equal(CatalogContentsState.Incomplete, savedRoot.ContentsState);
        Assert.Equal("Connection timeout after 3 retries", savedRoot.Properties["ScanErrorSummary"]?.ToString());
    }

    [Fact]
    public void CatalogEntryMerger_PreservesExistingIdsAndUserMetadata_OnRescan()
    {
        var existingRoot = new CatalogDocument
        {
            Id = "existing-root-id",
            Name = "MyMusic",
            ParentId = "library",
            Type = CatalogDocumentType.Folder,
            Status = "Reviewed",
            Properties = new PropertiesCollection
            {
                ["Comments"] = "User custom comment",
                ["Tags"] = "Rock, 90s",
                ["CustomRating"] = "5"
            }
        };

        var existingTrack = new CatalogDocument
        {
            Id = "existing-track-id",
            Name = "track1.mp3",
            ParentId = "existing-root-id",
            Type = CatalogDocumentType.File,
            Size = 100,
            Properties = new PropertiesCollection
            {
                ["Comments"] = "Best song"
            }
        };

        var stagedRoot = new CatalogDocument
        {
            Id = "new-staged-root-id",
            Name = "MyMusic",
            ParentId = "library",
            Type = CatalogDocumentType.Folder,
            Size = 500,
            ChildrenCount = 2,
            Properties = new PropertiesCollection
            {
                ["ScannedFormat"] = "FAT32"
            }
        };

        var stagedTrack = new CatalogDocument
        {
            Id = "new-staged-track-id",
            Name = "track1.mp3",
            ParentId = "new-staged-root-id",
            Type = CatalogDocumentType.File,
            Size = 200, // Updated size from rescan
            Properties = new PropertiesCollection
            {
                ["Bitrate"] = "320kbps"
            }
        };

        var merger = new CatalogEntryMerger();
        var merged = merger.Merge(
            [stagedRoot, stagedTrack],
            [existingRoot, existingTrack],
            preserveUserMetadata: true);

        Assert.Equal(2, merged.Count);

        var mergedRoot = merged.First(d => d.Name == "MyMusic");
        Assert.Equal("existing-root-id", mergedRoot.Id);
        Assert.Equal("Reviewed", mergedRoot.Status);
        Assert.Equal("User custom comment", mergedRoot.Properties["Comments"]?.ToString());
        Assert.Equal("Rock, 90s", mergedRoot.Properties["Tags"]?.ToString());
        Assert.Equal("5", mergedRoot.Properties["CustomRating"]?.ToString());
        Assert.Equal("FAT32", mergedRoot.Properties["ScannedFormat"]?.ToString());

        var mergedTrack = merged.First(d => d.Name == "track1.mp3");
        Assert.Equal("existing-track-id", mergedTrack.Id);
        Assert.Equal("existing-root-id", mergedTrack.ParentId); // Remapped parent ID!
        Assert.Equal(200, mergedTrack.Size); // Updated size
        Assert.Equal("Best song", mergedTrack.Properties["Comments"]?.ToString());
        Assert.Equal("320kbps", mergedTrack.Properties["Bitrate"]?.ToString());
    }

    [Fact]
    public async Task PublishScanAction_UndoAndRedo_WorksAtomically()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;

        // Existing document before scan
        var existingDoc = new CatalogDocument
        {
            Id = "existing-item",
            Name = "OldName",
            ParentId = "library",
            Type = CatalogDocumentType.Folder,
            Size = 50
        };
        repository.Save(existingDoc.Id, existingDoc);

        // New scanned item
        var insertedDoc = new CatalogDocument
        {
            Id = "new-item",
            Name = "NewScannedItem",
            ParentId = "library",
            Type = CatalogDocumentType.File,
            Size = 100
        };

        // Updated version of existing item
        var updatedDoc = new CatalogDocument
        {
            Id = existingDoc.Id,
            Name = existingDoc.Name,
            ParentId = existingDoc.ParentId,
            Type = existingDoc.Type,
            Size = 500
        };

        // Save updated and inserted
        repository.Save(insertedDoc.Id, insertedDoc);
        repository.Save(updatedDoc.Id, updatedDoc);

        var action = new PublishScanAction(
            repository,
            insertedDocuments: [insertedDoc],
            updatedDocuments: [(existingDoc, updatedDoc)]);

        // 1. Undo
        await action.UndoAsync();

        Assert.Null(repository.Get(insertedDoc.Id));
        var restored = repository.Get(existingDoc.Id);
        Assert.NotNull(restored);
        Assert.Equal(50, restored.Size);

        // 2. Redo
        await action.RedoAsync();

        Assert.NotNull(repository.Get(insertedDoc.Id));
        var redone = repository.Get(existingDoc.Id);
        Assert.NotNull(redone);
        Assert.Equal(500, redone.Size);
    }

    [Fact]
    public void StagingStore_CleanupAbandonedSessions_RemovesOldSessions()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);

        var oldSession = store.CreateSession(@"C:\Old", "library", "Old");
        var activeSession = store.CreateSession(@"C:\Active", "library", "Active");

        oldSession.Status = ScanStagingSessionStatus.Abandoned;
        store.UpdateSession(oldSession);

        var cleanedCount = store.CleanupAbandonedSessions(TimeSpan.FromHours(1));
        Assert.True(cleanedCount >= 1);

        Assert.Null(store.GetSession(oldSession.SessionId));
        Assert.NotNull(store.GetSession(activeSession.SessionId));
    }

    [Fact]
    public void MainWindowViewModel_PublishStagedScan_UpdatesUIAndRecordsUndo()
    {
        using var setup = InitializeRepositories();
        var repository = setup.CatalogRepository;
        using var viewModel = new MainWindowViewModel(setup.RepositoryManager);

        using var store = new CouchbaseScanStagingStore(repository.Collection.Database);
        var session = store.CreateSession(@"C:\StagedUI", "library", "StagedUI");

        var doc1 = new CatalogDocument
        {
            Id = $"ui-root-{Guid.NewGuid():N}",
            Name = "StagedUI",
            ParentId = "library",
            Type = CatalogDocumentType.Folder
        };
        var doc2 = new CatalogDocument
        {
            Id = $"ui-file-{Guid.NewGuid():N}",
            Name = "staged_file.txt",
            ParentId = doc1.Id,
            Type = CatalogDocumentType.File,
            Size = 250
        };

        store.StageDocuments(session.SessionId, [doc1, doc2]);

        var result = viewModel.PublishStagedScan(store, session.SessionId);
        Assert.NotNull(result);
        Assert.Equal(2, result.PublishedDocumentsCount);

        Assert.True(viewModel.IsDirtyDocument);
        Assert.True(viewModel.CanUndo);
        Assert.Contains(viewModel.BrowserItems, item => item.Name == "StagedUI");
    }

    [Fact]
    public void StagingStore_Exceptions_AreThrownOnInvalidOperations()
    {
        using var store = new InMemoryScanStagingStore();

        // 1. Not found
        Assert.Throws<StagingSessionNotFoundException>(() => store.StageDocuments("non-existent", []));
        Assert.Throws<StagingSessionNotFoundException>(() => store.GetStagedDocuments("non-existent"));

        // 2. Already committed
        var session = store.CreateSession("path", "parent", "name");
        session.Status = ScanStagingSessionStatus.Committed;
        store.UpdateSession(session);

        Assert.Throws<StagingSessionAlreadyCommittedException>(() => store.StageDocuments(session.SessionId, []));

        // 3. Already closed / cancelled
        var session2 = store.CreateSession("path", "parent", "name");
        session2.Status = ScanStagingSessionStatus.Cancelled;
        store.UpdateSession(session2);

        Assert.Throws<StagingSessionAlreadyClosedException>(() => store.StageDocuments(session2.SessionId, []));
    }
}
