using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Staging;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers undo and redo of catalog deletions and published imports against a real Couchbase Lite database:
/// whole-subtree restoration, redo that does not follow a stale selection, preview artifacts held for as long
/// as history can still restore them, capacity behaviour, storage failures and reopening the catalog.
/// </summary>
public sealed class CatalogDeletionUndoIntegrationTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    // ---------------------------------------------------------------- nested and multi-selection deletion

    [Fact]
    public async Task UndoAfterNestedFolderDeletion_RestoresTheWholeSubtreeWithIdsPropertiesAndTags()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        var scifi = viewModel.BrowserItems.Single(item => item.Id == "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { scifi };

        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        Assert.Null(context.Catalog.Get("scifi"));
        Assert.Null(context.Catalog.Get("interstellar"));
        Assert.Null(context.Catalog.Get("arrival"));
        Assert.True(viewModel.CanUndo);

        await viewModel.UndoCommand.ExecuteAsync(null);

        var restoredFolder = context.Catalog.Get("scifi");
        Assert.NotNull(restoredFolder);
        Assert.Equal("Sci-Fi", restoredFolder!.Name);
        Assert.Equal("movies", restoredFolder.ParentId);
        Assert.Equal(CatalogDocumentType.Folder, restoredFolder.Type);

        var restoredChild = context.Catalog.Get("interstellar");
        Assert.NotNull(restoredChild);
        Assert.Equal("scifi", restoredChild!.ParentId);
        Assert.Equal(4096, restoredChild.Size);

        // Tags and custom properties live in Properties, so restoring the document restores them verbatim.
        Assert.Equal("sci-fi, space", restoredChild.Properties["Tags"]);
        Assert.Equal("Nolan", restoredChild.Properties["Director"]);
        Assert.Equal(context.SourceFile, restoredChild.Properties["FullName"]);
        Assert.Equal("Watched", restoredChild.Status);

        Assert.NotNull(context.Catalog.Get("arrival"));
    }

    [Fact]
    public async Task UndoAfterMultiSelectionDeletion_RestoresEverySelectedSubtree()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        var scifi = viewModel.BrowserItems.Single(item => item.Id == "scifi");
        var drama = viewModel.BrowserItems.Single(item => item.Id == "drama");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { scifi, drama };

        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        foreach (var id in new[] { "scifi", "interstellar", "arrival", "drama", "drama-film" })
        {
            Assert.Null(context.Catalog.Get(id));
        }

        await viewModel.UndoCommand.ExecuteAsync(null);

        foreach (var id in new[] { "scifi", "interstellar", "arrival", "drama", "drama-film" })
        {
            Assert.NotNull(context.Catalog.Get(id));
        }
    }

    [Fact]
    public async Task UndoAfterTreeNodeDeletion_RestoresTheSubtree()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "scifi");

        await viewModel.DeleteItemCommand.ExecuteAsync("tree");

        Assert.Null(context.Catalog.Get("interstellar"));
        Assert.Null(context.Catalog.Get("scifi"));

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.NotNull(context.Catalog.Get("scifi"));
        Assert.NotNull(context.Catalog.Get("interstellar"));
        Assert.NotNull(context.Catalog.Get("arrival"));
    }

    [Fact]
    public async Task UndoAfterTreeNodeDeletion_RestoresTheNodeAndItsChildrenInTheTree()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "scifi");

        await viewModel.DeleteItemCommand.ExecuteAsync("tree");

        Assert.Null(context.FindTreeNode(viewModel, "scifi"));
        Assert.True(viewModel.CanUndo);

        await viewModel.UndoCommand.ExecuteAsync(null);

        var restored = context.FindTreeNode(viewModel, "scifi");
        Assert.NotNull(restored);
        Assert.Contains(context.SelectTreeNode(viewModel, "movies").Children, child => child.Key == "scifi");

        // The node has to come back as the folder it was, not as an empty shell. The tree only shows
        // folders and media, so the browser pane is what proves the restored document kept its type and
        // parent and that its file children came back with it.
        viewModel.SelectedTreeNode = restored;
        Assert.Equal(2, viewModel.BrowserItems.Count);
        Assert.Equal(CatalogDocumentType.Folder, context.Catalog.Get("scifi")!.Type);
    }

    [Fact]
    public async Task UndoAfterBrowserFolderDeletion_RestoresTheNodeAndItsChildrenInTheTree()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == "scifi");
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { viewModel.SelectedBrowserItem! };

        await viewModel.DeleteItemCommand.ExecuteAsync("list");

        Assert.Null(context.FindTreeNode(viewModel, "scifi"));
        Assert.True(viewModel.CanUndo);

        await viewModel.UndoCommand.ExecuteAsync(null);

        var restored = context.FindTreeNode(viewModel, "scifi");
        Assert.NotNull(restored);
        Assert.Contains(context.SelectTreeNode(viewModel, "movies").Children, child => child.Key == "scifi");

        viewModel.SelectedTreeNode = restored;
        Assert.Equal(2, viewModel.BrowserItems.Count);
        Assert.Equal(CatalogDocumentType.Folder, context.Catalog.Get("scifi")!.Type);
    }

    [Fact]
    public async Task TreeDeletionOfANodeWhoseRecordIsAlreadyGone_DoesNotFabricateAnUndoSnapshot()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        // Whatever made the tree stale, this is the state the user can reach: the record is gone but the
        // node is still on screen and still selectable.
        context.DeleteRecord("scifi");
        var staleNode = context.SelectTreeNode(viewModel, "scifi");

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = staleNode;

        await viewModel.DeleteItemCommand.ExecuteAsync("tree");

        // The pane is cleaned up, but nothing may be presented as reversible: there was no record left to
        // restore. A placeholder snapshot would come back as Type=File with no ParentId, which the tree
        // filters out of view — reporting "undone" while the tree shows nothing at all.
        Assert.Null(context.FindTreeNode(viewModel, "scifi"));
        Assert.False(viewModel.CanUndo);
        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.DeletedStaleEntry, "Sci-Fi"),
            viewModel.StatusText);

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Null(context.Catalog.Get("scifi"));
        Assert.Null(context.FindTreeNode(viewModel, "scifi"));
    }

    [Fact]
    public async Task UndoAfterDeepMediaTreeNodeDeletion_RestoresTheNodeInTheTree()
    {
        using var context = new TestContext();
        context.SeedCatalog();

        // The shape real catalogs have and the folder-only seed does not: Media nodes several levels down,
        // with Media children of their own. The tree admits Folder, NetworkFolder and Media, so a Type
        // that does not survive the round trip would make the restored node vanish from the pane.
        context.Catalog.Save("series", new CatalogDocument
        {
            Id = "series",
            Name = "Series",
            ParentId = "scifi",
            Type = CatalogDocumentType.Folder
        });
        context.Catalog.Save("disc-1", new CatalogDocument
        {
            Id = "disc-1",
            Name = "Disc 1",
            ParentId = "series",
            Type = CatalogDocumentType.Media
        });
        context.Catalog.Save("reel-1", new CatalogDocument
        {
            Id = "reel-1",
            Name = "Reel 1",
            ParentId = "disc-1",
            Type = CatalogDocumentType.Media
        });

        using var viewModel = context.CreateViewModel();

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "disc-1");

        await viewModel.DeleteItemCommand.ExecuteAsync("tree");

        Assert.Null(context.FindTreeNode(viewModel, "disc-1"));
        Assert.True(viewModel.CanUndo);

        await viewModel.UndoCommand.ExecuteAsync(null);

        var restored = context.FindTreeNode(viewModel, "disc-1");
        Assert.NotNull(restored);
        Assert.Equal(CatalogDocumentType.Media, context.Catalog.Get("disc-1")!.Type);
        Assert.Equal("series", context.Catalog.Get("disc-1")!.ParentId);
        Assert.Contains(restored!.Children, child => child.Key == "reel-1");
        Assert.Contains(context.SelectTreeNode(viewModel, "series").Children, child => child.Key == "disc-1");
    }

    [Fact]
    public async Task RedoAfterUndo_RemovesTheSameRecordsAndIgnoresTheCurrentSelection()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        var scifi = viewModel.BrowserItems.Single(item => item.Id == "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { scifi };
        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.NotNull(context.Catalog.Get("interstellar"));

        // Select something unrelated before redoing. Redo replays the captured ids, so the new selection must
        // not be what gets deleted.
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "music");
        var unrelated = viewModel.BrowserItems.Single(item => item.Id == "track-1");
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { unrelated };

        await viewModel.RedoCommand.ExecuteAsync(null);

        Assert.Null(context.Catalog.Get("scifi"));
        Assert.Null(context.Catalog.Get("interstellar"));
        Assert.Null(context.Catalog.Get("arrival"));
        Assert.NotNull(context.Catalog.Get("track-1"));
    }

    [Fact]
    public async Task UndoRedoTwice_IsIdempotentAcrossCycles()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument>
        {
            viewModel.BrowserItems.Single(item => item.Id == "scifi")
        };
        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            await viewModel.UndoCommand.ExecuteAsync(null);
            Assert.NotNull(context.Catalog.Get("interstellar"));
            Assert.Equal("scifi", context.Catalog.Get("interstellar")!.ParentId);

            await viewModel.RedoCommand.ExecuteAsync(null);
            Assert.Null(context.Catalog.Get("interstellar"));
        }
    }

    // ---------------------------------------------------------------- import publication

    [Fact]
    public async Task PublishedImport_IsReversibleAsOneOperation()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        var imported = new List<CatalogDocument>
        {
            new() { Id = "imported-media", Name = "Imported Disc", ParentId = "library", Type = CatalogDocumentType.Media },
            new() { Id = "imported-child", Name = "file.bin", ParentId = "imported-media", Type = CatalogDocumentType.File, Size = 10 }
        };

        await viewModel.AddImportedDocumentsAsync(imported);

        Assert.NotNull(context.Catalog.Get("imported-child"));
        Assert.True(viewModel.CanUndo);

        await viewModel.UndoCommand.ExecuteAsync(null);

        // One undo reverses the whole import, not just its root.
        Assert.Null(context.Catalog.Get("imported-media"));
        Assert.Null(context.Catalog.Get("imported-child"));

        await viewModel.RedoCommand.ExecuteAsync(null);

        Assert.NotNull(context.Catalog.Get("imported-media"));
        Assert.NotNull(context.Catalog.Get("imported-child"));
    }

    [Fact]
    public async Task Publisher_ReportsWhenAnImportIsTooLargeToBeUndoable()
    {
        using var context = new TestContext();
        context.SeedCatalog();

        var history = new SkyCD.Core.UndoRedo.UndoRedoHistory(maxHistorySize: 10)
        {
            // One document's worth of budget, so a two-document import cannot be recorded.
            MaxActionFootprintBytes = SkyCD.Core.UndoRedo.UndoFootprint.BytesPerDocument
        };

        var documents = new List<CatalogDocument>
        {
            new() { Id = "big-1", Name = "one", ParentId = "library", Type = CatalogDocumentType.File },
            new() { Id = "big-2", Name = "two", ParentId = "library", Type = CatalogDocumentType.File },
            new() { Id = "big-3", Name = "three", ParentId = "library", Type = CatalogDocumentType.File }
        };

        var result = new CatalogScanPublisher().PublishDirect(documents, context.Catalog, new ScanPublishOptions(), history);

        // The documents are published; what is refused is the promise that they can be taken back.
        Assert.Equal(3, result.PublishedDocumentsCount);
        Assert.NotNull(context.Catalog.Get("big-3"));
        Assert.False(result.IsUndoRecorded);
        Assert.False(history.CanUndo);
    }

    // ---------------------------------------------------------------- capacity and feedback

    [Fact]
    public async Task HistoryCapacityEviction_ReleasesCachePinsAndReportsToTheUser()
    {
        using var context = new TestContext();
        context.SeedCatalog();

        var manager = new CatalogUndoManager(context.Catalog, maxHistorySize: 2);
        var evicted = new List<string>();
        manager.ActionEvicted += (_, args) => evicted.Add(args.DisplayName);

        var scifi = context.Catalog.Get("scifi")!;
        var interstellar = context.Catalog.Get("interstellar")!;

        Assert.True(manager.RecordDeletion([scifi, interstellar]));
        Assert.True(manager.CachePins.IsPinned("interstellar"));

        // Two more deletions push the first snapshot out of a history of size two.
        manager.RecordDeletion([context.Catalog.Get("drama")!]);
        manager.RecordDeletion([context.Catalog.Get("track-1")!]);

        Assert.NotEmpty(evicted);
        Assert.False(manager.CachePins.IsPinned("interstellar"));
        Assert.Equal(2, manager.History.UndoCount);
    }

    [Fact]
    public async Task DeletionTooLargeForHistory_IsAppliedButReportedAsNotUndoable()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.UndoManager!.MaxActionFootprintBytes = SkyCD.Core.UndoRedo.UndoFootprint.BytesPerDocument;

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument>
        {
            viewModel.BrowserItems.Single(item => item.Id == "scifi")
        };

        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        // Applied, but never presented as reversible.
        Assert.Null(context.Catalog.Get("interstellar"));
        Assert.False(viewModel.CanUndo);
        Assert.StartsWith(
            LocalizationService.Current.Format(TextKeys.DeletedNotUndoable, "Sci-Fi").Split('.')[0],
            viewModel.StatusText,
            StringComparison.Ordinal);

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Null(context.Catalog.Get("interstellar"));
    }

    [Fact]
    public async Task DeletingALargeFolder_ReportsProgressAndUndoRestoresEveryRecord()
    {
        using var context = new TestContext();
        context.SeedCatalog();

        const int subfolderCount = 5;
        const int fileCount = 500;
        var removedIds = new List<string> { "bulk" };
        context.Catalog.Save("bulk", new CatalogDocument
        {
            Id = "bulk",
            Name = "Bulk",
            ParentId = "movies",
            Type = CatalogDocumentType.Folder
        });
        for (var subfolder = 0; subfolder < subfolderCount; subfolder++)
        {
            var id = $"bulk-{subfolder}";
            context.Catalog.Save(id, new CatalogDocument
            {
                Id = id,
                Name = $"Bulk {subfolder}",
                ParentId = "bulk",
                Type = CatalogDocumentType.Folder
            });
            removedIds.Add(id);
        }

        for (var file = 0; file < fileCount; file++)
        {
            var id = $"bulk-file-{file}";
            context.Catalog.Save(id, new CatalogDocument
            {
                Id = id,
                Name = $"File {file}.mkv",
                ParentId = $"bulk-{file % subfolderCount}",
                Type = CatalogDocumentType.File,
                Size = 100
            });
            removedIds.Add(id);
        }

        using var viewModel = context.CreateViewModel();
        var observed = new List<(bool Visible, int Value)>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.IsProgressVisible) or
                nameof(MainWindowViewModel.ProgressValue))
            {
                lock (observed) observed.Add((viewModel.IsProgressVisible, viewModel.ProgressValue));
            }
        };

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "bulk");

        await viewModel.DeleteItemCommand.ExecuteAsync("tree");

        Assert.All(removedIds, id => Assert.Null(context.Catalog.Get(id)));
        Assert.NotNull(context.Catalog.Get("movies"));
        Assert.True(viewModel.CanUndo);
        Assert.False(viewModel.IsProgressVisible);
        Assert.Equal(0, viewModel.ProgressValue);
        lock (observed)
        {
            Assert.Contains(observed, entry => entry.Visible);
            Assert.Contains(observed, entry => entry.Visible && entry.Value is > 0 and < 100);
        }

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.All(removedIds, id => Assert.NotNull(context.Catalog.Get(id)));
        Assert.Equal("bulk-0", context.Catalog.Get("bulk-file-0")!.ParentId);
        Assert.Equal("movies", context.Catalog.Get("bulk")!.ParentId);
    }

    // ---------------------------------------------------------------- preview artifact retention

    [Fact]
    public async Task PreviewArtifacts_SurviveRetentionWhileHistoryCanStillRestoreTheEntry()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        SavePreviewArtifact(viewModel.MediaCache, "interstellar");

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument>
        {
            viewModel.BrowserItems.Single(item => item.Id == "scifi")
        };
        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        Assert.Null(context.Catalog.Get("interstellar"));

        // Orphaned by the catalog, but undo can still bring the entry back with the same id, name and
        // ancestor chain, so its fingerprint still resolves and the artifact must not be reclaimed yet.
        viewModel.ReclaimUnreferencedPreviewArtifacts();
        Assert.NotEmpty(viewModel.MediaCache.GetRecordsForEntry("interstellar"));

        // Once history lets go, the same pass reclaims it.
        viewModel.UndoManager!.Clear();
        viewModel.ReclaimUnreferencedPreviewArtifacts();
        Assert.Empty(viewModel.MediaCache.GetRecordsForEntry("interstellar"));
    }

    [Fact]
    public async Task PreviewArtifacts_SurviveAnUndoRedoCycle()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        SavePreviewArtifact(viewModel.MediaCache, "interstellar");

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument>
        {
            viewModel.BrowserItems.Single(item => item.Id == "scifi")
        };
        await viewModel.DeleteItemCommand.ExecuteAsync(null);
        await viewModel.UndoCommand.ExecuteAsync(null);

        var snapshot = new MediaPreviewResolver(viewModel.MediaCache).Resolve(context.Catalog.Get("interstellar")!);
        Assert.True(snapshot.HasPayload);
    }

    // ---------------------------------------------------------------- failure handling

    [Fact]
    public async Task StorageFailureDuringUndo_IsReportedAndDoesNotEscapeTheCommand()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument>
        {
            viewModel.BrowserItems.Single(item => item.Id == "scifi")
        };
        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        // Closing the database underneath the view model is the storage failure a detached drive produces.
        context.CloseDatabase();

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.StartsWith(
            LocalizationService.Current.Get(TextKeys.UndoFailed).Split(':')[0],
            viewModel.StatusText,
            StringComparison.Ordinal);

        // The history rolled the action back onto the undo stack, so it stays offerable rather than lost.
        Assert.True(viewModel.CanUndo);
    }

    [Fact]
    public async Task UndoOfADeletion_PersistsAcrossCatalogReopen()
    {
        using var context = new TestContext();
        context.SeedCatalog();

        using (var viewModel = context.CreateViewModel())
        {
            viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
            viewModel.ActivePane = ActivePane.Browser;
            viewModel.SelectedBrowserItems = new List<CatalogDocument>
            {
                viewModel.BrowserItems.Single(item => item.Id == "scifi")
            };
            await viewModel.DeleteItemCommand.ExecuteAsync(null);
            await viewModel.UndoCommand.ExecuteAsync(null);
        }

        using var reopened = context.CreateViewModel();

        Assert.NotNull(context.Catalog.Get("interstellar"));
        Assert.False(reopened.CanUndo);
        Assert.False(reopened.CanRedo);
    }

    [Fact]
    public async Task Deletion_PersistsAcrossCatalogReopen()
    {
        using var context = new TestContext();
        context.SeedCatalog();

        using (var viewModel = context.CreateViewModel())
        {
            viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
            viewModel.ActivePane = ActivePane.Browser;
            viewModel.SelectedBrowserItems = new List<CatalogDocument>
            {
                viewModel.BrowserItems.Single(item => item.Id == "scifi")
            };
            await viewModel.DeleteItemCommand.ExecuteAsync(null);
        }

        using var reopened = context.CreateViewModel();

        Assert.Null(context.Catalog.Get("scifi"));
        Assert.Null(context.Catalog.Get("interstellar"));
        Assert.False(reopened.CanUndo);
    }

    [Fact]
    public async Task DeletingCatalogEntries_NeverTouchesTheFilesystem()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();

        Assert.True(File.Exists(context.SourceFile));

        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "movies");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = new List<CatalogDocument>
        {
            viewModel.BrowserItems.Single(item => item.Id == "scifi")
        };
        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        Assert.Null(context.Catalog.Get("interstellar"));
        Assert.True(File.Exists(context.SourceFile));
    }

    [Fact]
    public async Task AcceptedProperties_AreOneDurableUndoStepAndKeepLogicalSelection()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == "interstellar");
        PropertiesDialogRequestedEventArgs? request = null;
        viewModel.PropertiesRequested += (_, args) => request = args;
        viewModel.OpenPropertiesCommand.Execute(null);
        Assert.NotNull(request);
        request.Dialog.Name = "Renamed.mkv";
        request.Dialog.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = "Director", DefaultValue = "" }]);
        request.Dialog.CustomProperties.Single().DefaultValue = "Villeneuve";
        request.Complete(true, "Offline comment");

        Assert.Equal(1, viewModel.UndoManager!.History.UndoCount);
        Assert.Equal("Renamed.mkv", context.Catalog.Get("interstellar")!.Name);
        Assert.Equal("Offline comment", context.Catalog.Get("interstellar")!.Properties["Comment"]);
        Assert.Equal("Villeneuve", context.Catalog.Get("interstellar")!.Properties["Director"]);
        Assert.True(viewModel.UndoCommand.CanExecute(null));

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Interstellar.mkv", context.Catalog.Get("interstellar")!.Name);
        Assert.False(context.Catalog.Get("interstellar")!.Properties.ContainsKey("Comment"));
        Assert.Equal("Nolan", context.Catalog.Get("interstellar")!.Properties["Director"]);
        Assert.Equal("interstellar", viewModel.SelectedBrowserItem?.Id);
        Assert.True(viewModel.RedoCommand.CanExecute(null));
        await viewModel.RedoCommand.ExecuteAsync(null);
        using var reopened = new TestContext();
        Assert.Equal("Renamed.mkv", reopened.Catalog.Get("interstellar")!.Name);
        Assert.Equal("Offline comment", reopened.Catalog.Get("interstellar")!.Properties["Comment"]);
        Assert.Equal("Villeneuve", reopened.Catalog.Get("interstellar")!.Properties["Director"]);
    }

    [Fact]
    public void Properties_HideGeneralFieldsAndPreserveStoredValuesAcrossRename()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        var entry = context.Catalog.Get("interstellar")!;
        entry.Properties["Name"] = "Original name";
        entry.Properties["RealDirectoryName"] = "Original directory";
        entry.Properties["Comment"] = "Original comment";
        entry.Properties["Comments"] = "Imported comments";
        entry.Properties["FullName"] = "/movies/interstellar.mkv";
        context.Catalog.Save(entry.Id, entry);
        using var viewModel = context.CreateViewModel();
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == entry.Id);
        PropertiesDialogRequestedEventArgs? request = null;
        viewModel.PropertiesRequested += (_, args) => request = args;

        viewModel.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal("Original comment", request.Dialog.Comments);
        Assert.False(request.Dialog.InfoProperties.ContainsKey("Name"));
        Assert.False(request.Dialog.InfoProperties.ContainsKey("RealDirectoryName"));
        Assert.False(request.Dialog.InfoProperties.ContainsKey("Comment"));
        Assert.Equal("Imported comments", request.Dialog.InfoProperties["Comments"]);
        Assert.Equal("/movies/interstellar.mkv", request.Dialog.InfoProperties[MediaMetadataValueFormatter.ResolveLabel("FullName")]);
        request.Dialog.ApplyCustomPropertyDefaults([new CustomPropertyDocument { Name = "name", DefaultValue = "fallback" }]);
        Assert.Equal("Original name", Assert.Single(request.Dialog.CustomProperties).DefaultValue);
        request.Dialog.Name = "Renamed.mkv";
        request.Complete(false, "Canceled comment");
        Assert.Equal("Interstellar.mkv", context.Catalog.Get(entry.Id)!.Name);
        Assert.Equal("Original comment", context.Catalog.Get(entry.Id)!.Properties["Comment"]);

        request = null;
        viewModel.OpenPropertiesCommand.Execute(null);
        Assert.NotNull(request);
        request.Dialog.Name = "Renamed.mkv";
        request.Complete(true, request.Dialog.Comments);
        using var reopened = new TestContext();
        var saved = reopened.Catalog.Get(entry.Id)!;
        Assert.Equal("Renamed.mkv", saved.Name);
        Assert.Equal("Original name", saved.Properties["Name"]);
        Assert.Equal("Original directory", saved.Properties["RealDirectoryName"]);
        Assert.Equal("Original comment", saved.Properties["Comment"]);
        Assert.Equal("Imported comments", saved.Properties["Comments"]);
        Assert.Equal("/movies/interstellar.mkv", saved.Properties["FullName"]);
    }

    [Fact]
    public void CanceledProperties_LeaveStorageAndHistoryUntouched()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        using var viewModel = context.CreateViewModel();
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == "interstellar");
        viewModel.PropertiesRequested += (_, args) =>
        {
            args.Dialog.Name = "Canceled";
            args.Complete(false, "Canceled");
        };
        viewModel.OpenPropertiesCommand.Execute(null);
        Assert.False(viewModel.CanUndo);
        Assert.Equal("Interstellar.mkv", context.Catalog.Get("interstellar")!.Name);
        Assert.False(context.Catalog.Get("interstellar")!.Properties.ContainsKey("Comment"));
    }

    [Fact]
    public async Task BulkStatusClear_UndoesStatusAndBadgePropertiesInOneStep()
    {
        using var context = new TestContext();
        context.SeedCatalog();
        var original = context.Catalog.Get("interstellar")!;
        original.Properties["StatusIconGlyph"] = "film";
        context.Catalog.Save(original.Id, original);
        using var viewModel = context.CreateViewModel();
        viewModel.SelectedTreeNode = context.SelectTreeNode(viewModel, "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItems = viewModel.BrowserItems.ToList();
        viewModel.ApplyBrowserItemStatusCommand.Execute(null);
        Assert.Equal(1, viewModel.UndoManager!.History.UndoCount);
        Assert.Null(context.Catalog.Get("interstellar")!.Status);
        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Watched", context.Catalog.Get("interstellar")!.Status);
        Assert.Equal("film", context.Catalog.Get("interstellar")!.Properties["StatusIconGlyph"]);
        Assert.False(viewModel.CanUndo);
        await viewModel.RedoCommand.ExecuteAsync(null);
        using var reopened = context.CreateViewModel();
        Assert.Null(context.Catalog.Get("interstellar")!.Status);
    }

    // ---------------------------------------------------------------- helpers

    private static void SavePreviewArtifact(IMediaCacheStore store, string entryId)
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var fingerprint = $"fingerprint-{entryId}";
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint,
            "plugin.video",
            "1.0.0",
            "Preview",
            "fake.preview",
            1),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = "plugin.video",
            PluginVersion = "1.0.0",
            Capability = "Preview",
            SchemaId = "fake.preview",
            SchemaVersion = 1,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow,
            SourceLength = payload.Length,
            Artifacts =
            [
                new MediaArtifactReference
                {
                    Key = "preview:0",
                    ContentType = "image/jpeg",
                    Length = payload.Length,
                    Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant()
                }
            ]
        }.WithRecalculatedSizes();

        store.Save(new MediaCachePayload(record, new Dictionary<string, byte[]> { ["preview:0"] = payload }));
    }

    /// <summary>Owns the container, database and repositories for one test, and the catalog they hold.</summary>
    private sealed class TestContext : IDisposable
    {
        private readonly Container container = new();
        private readonly string mediaDirectory;
        private bool databaseClosed;

        public TestContext()
        {
            new CouchbaseServiceRegistrator().RegisterServices(container);
            Repositories = container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)Repositories.For<CatalogDocument>();

            var options = (AppOptionsDocumentRepository)Repositories.For<AppOptionsDocument>();
            var appOptions = options.GetOrCreateAppOptions();
            appOptions.AppStartCount = 99;
            options.Save(AppOptionsDocument.DocumentId, appOptions);

            mediaDirectory = Path.Combine(Path.GetTempPath(), $"skycd-undo-media-{Guid.NewGuid():N}");
            Directory.CreateDirectory(mediaDirectory);
            SourceFile = Path.Combine(mediaDirectory, "interstellar.mkv");
            File.WriteAllBytes(SourceFile, new byte[4096]);
        }

        public RepositoryManager Repositories { get; }
        public CatalogDocumentRepository Catalog { get; }
        public string SourceFile { get; }

        public MainWindowViewModel CreateViewModel() => new(Repositories);

        /// <summary>Simulates the storage going away, so undo has to fail rather than corrupt state.</summary>
        public void CloseDatabase()
        {
            if (databaseClosed) return;
            databaseClosed = true;
            container.Resolve<DatabaseManager>().GetDatabase().Close();
        }

        public BrowserTreeNode SelectTreeNode(MainWindowViewModel viewModel, string key)
        {
            var node = FindTreeNode(viewModel, key);
            Assert.NotNull(node);
            return node!;
        }

        /// <summary>
        /// Removes a record behind the view model's back, which is how a tree ends up showing a node whose
        /// document no longer exists.
        /// </summary>
        public void DeleteRecord(string id)
        {
            using var stored = Catalog.Collection.GetDocument(id);
            if (stored is not null)
            {
                Catalog.Collection.Delete(stored);
            }
        }

        public BrowserTreeNode? FindTreeNode(MainWindowViewModel viewModel, string key) =>
            Flatten(viewModel.TreeNodes).FirstOrDefault(candidate => candidate.Key == key);

        public static IEnumerable<BrowserTreeNode> FlattenTree(IEnumerable<BrowserTreeNode> nodes) => Flatten(nodes);

        public void SeedCatalog()
        {
            ClearCatalog();

            Save(new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder });
            Save(new CatalogDocument { Id = "movies", Name = "Movies", ParentId = "library", Type = CatalogDocumentType.Folder });
            Save(new CatalogDocument { Id = "music", Name = "Music", ParentId = "library", Type = CatalogDocumentType.Folder });
            Save(new CatalogDocument { Id = "scifi", Name = "Sci-Fi", ParentId = "movies", Type = CatalogDocumentType.Folder });
            Save(new CatalogDocument { Id = "drama", Name = "Drama", ParentId = "movies", Type = CatalogDocumentType.Folder });

            Save(new CatalogDocument
            {
                Id = "interstellar",
                Name = "Interstellar.mkv",
                ParentId = "scifi",
                Type = CatalogDocumentType.File,
                Size = 4096,
                Status = "Watched",
                Properties = new PropertiesCollection
                {
                    ["FullName"] = SourceFile,
                    ["Tags"] = "sci-fi, space",
                    ["Director"] = "Nolan",
                    ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
                }
            });

            Save(new CatalogDocument
            {
                Id = "arrival",
                Name = "Arrival.mkv",
                ParentId = "scifi",
                Type = CatalogDocumentType.File,
                Size = 2048
            });

            Save(new CatalogDocument
            {
                Id = "drama-film",
                Name = "Drama Film.mkv",
                ParentId = "drama",
                Type = CatalogDocumentType.File,
                Size = 1024
            });

            Save(new CatalogDocument
            {
                Id = "track-1",
                Name = "track-01.flac",
                ParentId = "music",
                Type = CatalogDocumentType.File,
                Size = 512
            });
        }

        private void Save(CatalogDocument document) => Catalog.Save(document.Id, document);

        private void ClearCatalog()
        {
            foreach (var document in Catalog.GetAll())
            {
                using var stored = Catalog.Collection.GetDocument(document.Id);
                if (stored is not null)
                {
                    Catalog.Collection.Delete(stored);
                }
            }
        }

        private static IEnumerable<BrowserTreeNode> Flatten(IEnumerable<BrowserTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!databaseClosed)
                {
                    container.Resolve<DatabaseManager>().GetDatabase().Close();
                }
            }
            catch (Exception exception) when (exception is CouchbaseException or InvalidOperationException)
            {
                // Already closed by the test.
            }

            container.Dispose();

            try
            {
                if (Directory.Exists(mediaDirectory))
                {
                    Directory.Delete(mediaDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
