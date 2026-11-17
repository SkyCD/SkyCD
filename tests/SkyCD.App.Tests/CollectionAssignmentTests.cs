using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Collections;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.Staging;
using System.Threading;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CollectionAssignmentTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-collections-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public CollectionAssignmentTests()
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
        catch
        {
        }
    }

    private sealed class Stack : IDisposable
    {
        public Stack()
        {
            Container = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(Container);
            var repositories = Container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
            Collections = (VirtualCollectionDocumentRepository)repositories.For<VirtualCollectionDocument>();
            Service = new VirtualCollectionService(Catalog, Collections);
        }

        public Container Container { get; }

        public CatalogDocumentRepository Catalog { get; }

        public VirtualCollectionDocumentRepository Collections { get; }

        public VirtualCollectionService Service { get; }

        public void Dispose() => Container.Dispose();
    }

    private static Stack CreateStack() => new();

    private static CatalogDocument Entry(string id, string? parentId = null)
    {
        return new CatalogDocument
        {
            Id = id,
            Name = id,
            ParentId = parentId,
            Type = CatalogDocumentType.File,
            Size = 100,
            ChildrenCount = 0
        };
    }

    private static void DeleteEntry(Stack stack, string id)
    {
        using var stored = stack.Catalog.Collection.GetDocument(id);
        if (stored is not null)
        {
            stack.Catalog.Collection.Delete(stored);
        }
    }

    private static AdvancedCatalogQuery NameContainsQuery(string text)
    {
        return new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Name,
                        Operator = AdvancedCatalogOperator.Contains,
                        Value = text
                    }
                ]
            }
        };
    }

    [Fact]
    public void Memberships_LeaveOriginalParentAndPathUnchanged()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("root", Entry("root"));
        stack.Catalog.Save("file", Entry("file", "root"));
        var collection = stack.Service.CreateCollection("Favorites");

        stack.Service.AddEntries(collection.Id, ["file", "root"]);
        stack.Service.RemoveEntries(collection.Id, ["root"]);

        var file = stack.Catalog.Get("file")!;
        var root = stack.Catalog.Get("root")!;
        Assert.Equal("root", file.ParentId);
        Assert.Null(root.ParentId);
        Assert.Equal("file", file.Name);
        Assert.Equal(["file"], stack.Service.GetEntryIds(collection.Id));
        Assert.Equal("file", stack.Service.ResolveEntries(collection.Id, out var missing).Single().Id);
        Assert.Equal(0, missing);
    }

    [Fact]
    public void Memberships_SurviveRepositoryRecreationAndRestart()
    {
        using (var stack = CreateStack())
        {
            stack.Catalog.Save("root", Entry("root"));
            stack.Catalog.Save("file", Entry("file", "root"));
            var collection = stack.Service.CreateCollection("Favorites");
            stack.Service.AddEntries(collection.Id, ["file", "root"]);
        }

        using var reopened = CreateStack();
        var collectionId = reopened.Collections.FindByName("Favorites")!.Id;
        Assert.Equal(["file", "root"], reopened.Service.GetEntryIds(collectionId));
        var resolved = reopened.Service.ResolveEntries(collectionId, out var missing);
        Assert.Equal(0, missing);
        Assert.Equal(["file", "root"], resolved.Select(entry => entry.Id).OrderBy(static id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void DeletedEntries_AreSkippedDeterministicallyAndPruned()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("kept", Entry("kept"));
        stack.Catalog.Save("doomed", Entry("doomed"));
        var collection = stack.Service.CreateCollection("Mixed");
        stack.Service.AddEntries(collection.Id, ["kept", "doomed", "ghost"]);

        DeleteEntry(stack, "doomed");

        var resolved = stack.Service.ResolveEntries(collection.Id, out var missing);
        Assert.Equal(2, missing);
        Assert.Equal("kept", resolved.Single().Id);

        Assert.Equal(2, stack.Service.PruneDeletedReferences(collection.Id));
        Assert.Equal(["kept"], stack.Service.GetEntryIds(collection.Id));
        Assert.Equal(0, stack.Service.PruneDeletedReferences(collection.Id));
        stack.Service.ResolveEntries(collection.Id, out missing);
        Assert.Equal(0, missing);
    }

    [Fact]
    public void DuplicateMemberships_AreStoredOnce()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        var collection = stack.Service.CreateCollection("Singles");

        stack.Service.AddEntries(collection.Id, ["a", "a", " a "]);
        stack.Service.AddEntries(collection.Id, ["a"]);

        Assert.Equal(["a"], stack.Service.GetEntryIds(collection.Id));
    }

    [Fact]
    public void RemovingMembershipOrCollection_NeverDeletesEntries()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b", "a"));
        var collection = stack.Service.CreateCollection("Temp");
        stack.Service.AddEntries(collection.Id, ["a", "b"]);

        stack.Service.RemoveEntries(collection.Id, ["a"]);
        Assert.NotNull(stack.Catalog.Get("a"));

        stack.Service.DeleteCollection(collection.Id);
        Assert.NotNull(stack.Catalog.Get("a"));
        Assert.NotNull(stack.Catalog.Get("b"));
        Assert.Null(stack.Collections.Get(collection.Id));
        Assert.Throws<CollectionNotFoundException>(() => stack.Service.GetRequired(collection.Id));
    }

    [Fact]
    public void CollectionNames_AreValidatedAndUniqueCaseInsensitively()
    {
        using var stack = CreateStack();
        stack.Service.CreateCollection("Favorites");

        Assert.Throws<CollectionNameRequiredException>(() => stack.Service.CreateCollection("   "));
        Assert.Throws<CollectionDuplicateNameException>(() => stack.Service.CreateCollection("favorites"));
        Assert.Throws<CollectionDuplicateNameException>(() => stack.Service.CreateCollection("  FAVORITES  "));
        Assert.Throws<CollectionNotFoundException>(() => stack.Service.RenameCollection("nope", "Other"));
        Assert.Throws<CollectionNameRequiredException>(() => stack.Service.RenameCollection(
            stack.Collections.FindByName("Favorites")!.Id, "   "));
    }

    [Fact]
    public void Rename_KeepsMembershipsByStableId()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("file", Entry("file"));
        var collection = stack.Service.CreateCollection("Favorites");
        stack.Service.AddEntries(collection.Id, ["file"]);

        stack.Service.RenameCollection(collection.Id, "Best Ever");

        Assert.Equal("Best Ever", stack.Service.GetRequired(collection.Id).Name);
        Assert.Equal(["file"], stack.Service.GetEntryIds(collection.Id));
        Assert.Equal(
            [collection.Id],
            stack.Service.FindCollectionsContaining("file").Select(candidate => candidate.Id));
    }

    [Fact]
    public void DynamicCollections_EvaluateSavedSearchAndRejectStoredEdits()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("concert", Entry("concert-2025"));
        stack.Catalog.Save("report", Entry("report"));
        var dynamic = stack.Service.CreateDynamicCollection("Music", NameContainsQuery("concert"));

        var resolved = stack.Service.ResolveEntries(dynamic.Id, out var missing);
        Assert.Equal(0, missing);
        Assert.Equal("concert-2025", resolved.Single().Id);
        Assert.Empty(stack.Service.GetEntryIds(dynamic.Id));

        Assert.Throws<InvalidOperationException>(() => stack.Service.SetEntryIds(dynamic.Id, ["concert-2025"]));
        Assert.Throws<InvalidOperationException>(() => stack.Service.AddEntries(dynamic.Id, ["concert-2025"]));
        Assert.Throws<InvalidOperationException>(() => stack.Service.RemoveEntries(dynamic.Id, ["concert-2025"]));
        Assert.Throws<InvalidOperationException>(() => stack.Service.PruneDeletedReferences(dynamic.Id));
    }

    [Fact]
    public void DynamicQuery_RoundTripsThroughJsonAndSurvivesRestart()
    {
        var query = NameContainsQuery("concert");
        using (var stack = CreateStack())
        {
            stack.Service.CreateDynamicCollection("Music", query);
        }

        using var reopened = CreateStack();
        var collectionId = reopened.Collections.FindByName("Music")!.Id;
        var reloaded = reopened.Service.GetDynamicQuery(collectionId);
        Assert.Equal(
            VirtualCollectionService.SerializeQuery(query),
            VirtualCollectionService.SerializeQuery(reloaded));
        Assert.Equal("concert", reloaded.Root.Predicates.Single().Value?.ToString());
        Assert.Throws<CollectionNotFoundException>(() => reopened.Service.GetDynamicQuery("missing"));
    }

    private sealed class SyncDriveProvider(bool ready = true) : IMediaDriveProvider
    {
        public MediaDriveInfo Drive { get; } = new("test", "Test media", "test-root", DriveType.Removable,
            ready, 100, 100, 1000, "TEST");
        public IReadOnlyList<MediaDriveInfo> GetSupportedDrives() => [Drive];
        public MediaDriveInfo? GetDrive(string path) => Drive;
        public event EventHandler? DrivesChanged { add { } remove { } }
    }

    private static CatalogDocument SyncMedia(string id = "media", string name = "Test media") => new()
    {
        Id = id,
        Name = name,
        Type = CatalogDocumentType.Media,
        Properties = new SkyCD.Documents.Collections.PropertiesCollection
        {
            ["RootDirectory"] = "test-root",
            ["VolumeLabel"] = "Test media",
            ["TotalSize"] = "1000",
            ["DriveFormat"] = "TEST"
        }
    };

    [Fact]
    public async Task BackgroundMediaSync_MergesNewFilesAndPreservesUserMetadataWithoutDuplicates()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", SyncMedia());
        var existing = Entry("existing", "media");
        existing.Properties["Comment"] = "Keep offline";
        stack.Catalog.Save(existing.Id, existing);
        var publications = 0;
        using var service = new BackgroundMediaSyncService(stack.Catalog,
            (documents, token) =>
            {
                publications++;
                new CatalogScanPublisher().PublishDirect(documents, stack.Catalog,
                    new ScanPublishOptions { CancellationToken = token });
            }, drives: new SyncDriveProvider(), scan: (_, _) => new CatalogScanResult
            {
                Documents = [SyncMedia("scanned"), Entry("existing", "scanned"), Entry("new", "scanned")],
                Statistics = new ScanTraversalStatistics()
            });

        await service.RunOnceAsync();
        await service.RunOnceAsync();
        Assert.Equal(1, publications);
        Assert.Equal(3, stack.Catalog.GetAll().Count);
        Assert.Equal("media", stack.Catalog.Get("new")!.ParentId);
        Assert.Equal("Keep offline", stack.Catalog.Get("existing")!.Properties["Comment"]);
        using var reopened = CreateStack();
        Assert.NotNull(reopened.Catalog.Get("new"));
        Assert.Equal("Keep offline", reopened.Catalog.Get("existing")!.Properties["Comment"]);
    }

    [Fact]
    public async Task BackgroundMediaSync_SkipsOfflineMediaAndChangedCatalogRoots()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", SyncMedia());
        var scans = 0;
        var publications = 0;
        using (var offline = new BackgroundMediaSyncService(stack.Catalog, (_, _) => publications++,
            drives: new SyncDriveProvider(false), scan: (_, _) =>
            {
                scans++;
                return new CatalogScanResult { Documents = [], Statistics = new ScanTraversalStatistics() };
            }))
        {
            await offline.RunOnceAsync();
        }
        Assert.Equal(0, scans);
        using var changed = new BackgroundMediaSyncService(stack.Catalog, (_, _) => publications++,
            drives: new SyncDriveProvider(), scan: (_, _) =>
            {
                DeleteEntry(stack, "media");
                return new CatalogScanResult { Documents = [SyncMedia()], Statistics = new ScanTraversalStatistics() };
            });
        await changed.RunOnceAsync();
        Assert.Equal(0, publications);
    }

    [Fact]
    public async Task BackgroundMediaSync_CancellationAndOverlapNeverPublishPartialResults()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("media", SyncMedia());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var scans = 0;
        using var service = new BackgroundMediaSyncService(stack.Catalog,
            (_, _) => Assert.Fail("Canceled scan must not publish"), drives: new SyncDriveProvider(),
            scan: (_, token) =>
            {
                Interlocked.Increment(ref scans);
                started.SetResult();
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                token.ThrowIfCancellationRequested();
                return new CatalogScanResult { Documents = [], Statistics = new ScanTraversalStatistics() };
            });
        var running = service.RunOnceAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.RunOnceAsync();
        Assert.Equal(1, scans);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Single(stack.Catalog.GetAll());
    }

    [Fact]
    public async Task BackgroundMediaSync_OneFailedMediumDoesNotStarveOtherMedia()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("bad", SyncMedia("bad", "Bad"));
        stack.Catalog.Save("good", SyncMedia("good", "Good"));
        var publications = 0;
        using var service = new BackgroundMediaSyncService(stack.Catalog,
            (documents, token) =>
            {
                publications++;
                new CatalogScanPublisher().PublishDirect(documents, stack.Catalog);
            }, drives: new SyncDriveProvider(), scan: (options, _) => options.MediaName == "Bad"
                ? throw new IOException("Unavailable medium")
                : new CatalogScanResult
                {
                    Documents = [SyncMedia("scanned", "Good"), Entry("new", "scanned")],
                    Statistics = new ScanTraversalStatistics()
                });
        await service.RunOnceAsync();
        Assert.Equal(1, publications);
        Assert.Equal("good", stack.Catalog.Get("new")!.ParentId);
        Assert.Equal("Unavailable medium", service.LastError);
    }

    [Fact]
    public async Task MembershipChanges_UndoAndRedoThroughHistory()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b"));
        var collection = stack.Service.CreateCollection("Undoable");
        stack.Service.AddEntries(collection.Id, ["a"]);
        using var undo = new CatalogUndoManager(stack.Catalog);

        var before = stack.Service.GetEntryIds(collection.Id);
        stack.Service.SetEntryIds(collection.Id, ["a", "b"]);
        undo.RecordCollectionEntriesChanged(
            stack.Collections,
            collection.Id,
            before,
            stack.Service.GetEntryIds(collection.Id));

        await undo.UndoAsync();
        Assert.Equal(["a"], stack.Service.GetEntryIds(collection.Id));

        await undo.RedoAsync();
        Assert.Equal(["a", "b"], stack.Service.GetEntryIds(collection.Id));
    }
}
