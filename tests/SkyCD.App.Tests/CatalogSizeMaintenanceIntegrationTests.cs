using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Formatting;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers catalog size maintenance against a real Couchbase Lite database (#1001): an added entry raises the
/// totals of its ancestors without a rescan, a deletion lowers them in the background, unknown sizes stay
/// hidden instead of reading as a real zero, and undo puts the previous totals back.
/// </summary>
public sealed class CatalogSizeMaintenanceIntegrationTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [Fact]
    public async Task PublishingAnEntry_AccumulatesAncestorSizesWithoutRescanning()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();

        await viewModel.AddImportedDocumentsAsync(
        [
            new CatalogDocument
            {
                Id = "new-episode",
                Name = "Episode 1.mkv",
                ParentId = "scifi",
                Type = CatalogDocumentType.File,
                Size = 4096
            }
        ]);

        // 6144 already in Sci-Fi plus the newly published 4096; the whole ancestor chain follows.
        Assert.Equal(10240, context.Catalog.Get("scifi")!.Size);
        Assert.Equal(10240, context.Catalog.Get("movies")!.Size);
        Assert.Equal(10240 + 512, context.Catalog.Get("library")!.Size);
        Assert.Equal(SizeFormatting.FormatBytes(10240, "0.##"), context.Catalog.Get("movies")!.DisplaySize);
    }

    [Fact]
    public async Task DeletingAKnownEntry_RecalculatesAncestorSizesInTheBackground()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();

        SelectTreeNode(viewModel, "scifi");
        viewModel.ActivePane = ActivePane.Browser;
        var arrival = viewModel.BrowserItems.Single(item => item.Id == "arrival");
        viewModel.SelectedBrowserItem = arrival;
        viewModel.SelectedBrowserItems = new List<CatalogDocument> { arrival };

        await viewModel.DeleteItemCommand.ExecuteAsync(null);

        Assert.Null(context.Catalog.Get("arrival"));

        await WaitUntilAsync(
            () => context.Catalog.Get("scifi")?.Size == 4096 &&
                  context.Catalog.Get("movies")?.Size == 4096 &&
                  context.Catalog.Get("library")?.Size == 4096 + 512,
            "deleting Arrival should leave Sci-Fi at 4096 and lower its ancestors");
    }

    [Fact]
    public async Task DeletingAFolder_RecalculatesEachAncestorOnce()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = SelectTreeNode(viewModel, "scifi");

        await viewModel.DeleteItemCommand.ExecuteAsync("tree");

        Assert.Null(context.Catalog.Get("scifi"));
        Assert.Null(context.Catalog.Get("interstellar"));

        await WaitUntilAsync(
            () => context.Catalog.Get("movies")?.Size == 0 &&
                  context.Catalog.Get("library")?.Size == 512,
            "removing the whole Sci-Fi subtree should empty Movies and leave only Music under Library");
    }

    [Fact]
    public async Task UndoOfADeletion_RestoresTheAncestorSizes()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();

        viewModel.ActivePane = ActivePane.Tree;
        viewModel.SelectedTreeNode = SelectTreeNode(viewModel, "scifi");
        await viewModel.DeleteItemCommand.ExecuteAsync("tree");
        await WaitUntilAsync(
            () => context.Catalog.Get("movies")?.Size == 0,
            "the background recount should empty Movies before undo");

        await viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Equal(6144, context.Catalog.Get("scifi")!.Size);
        Assert.Equal(6144, context.Catalog.Get("movies")!.Size);
        Assert.Equal(6144 + 512, context.Catalog.Get("library")!.Size);

        await viewModel.RedoCommand.ExecuteAsync(null);

        Assert.Equal(0, context.Catalog.Get("movies")!.Size);
        Assert.Equal(512, context.Catalog.Get("library")!.Size);
    }

    [Fact]
    public void TitleOnlyMedia_LeavesItsOwnAndAncestorSizesUnknown()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();

        var document = viewModel.AddTitleOnlyMedia("Unmeasured", "scifi");

        var stored = context.Catalog.Get(document.Id)!;
        Assert.False(stored.HasKnownSize);
        Assert.Equal(string.Empty, stored.DisplaySize);

        // The folder can no longer state a total: one of its children has no measured size.
        var scifi = context.Catalog.Get("scifi")!;
        Assert.False(scifi.HasKnownSize);
        Assert.Equal(string.Empty, scifi.DisplaySize);
    }

    [Fact]
    public void Properties_ShowTheAccumulatedSizeForAFolderAndHideAnUnknownOne()
    {
        using var context = new TestContext();
        context.Catalog.Save("empty-shelf", new CatalogDocument
        {
            Id = "empty-shelf",
            Name = "Empty shelf",
            ParentId = "movies",
            Type = CatalogDocumentType.Folder
        });
        context.Catalog.Save("unmeasured", new CatalogDocument
        {
            Id = "unmeasured",
            Name = "Unmeasured.flac",
            ParentId = "music",
            Type = CatalogDocumentType.File,
            Properties = new PropertiesCollection { [CatalogDocument.SizeKnownPropertyKey] = false }
        });
        using var viewModel = context.CreateViewModel();

        SelectTreeNode(viewModel, "movies");
        var scifiRow = OpenProperties(viewModel, "scifi");
        Assert.Equal(
            $"{SizeFormatting.FormatBytes(6144, "0.##")} ({6144.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)} bytes)",
            scifiRow.InfoProperties["Size"]);

        var emptyRow = OpenProperties(viewModel, "empty-shelf");
        Assert.Equal("0 b (0 bytes)", emptyRow.InfoProperties["Size"]);

        SelectTreeNode(viewModel, "music");
        var unknownRow = OpenProperties(viewModel, "unmeasured");
        Assert.False(unknownRow.InfoProperties.ContainsKey("Size"));
    }

    [Fact]
    public void BrowserProjection_HidesTheSizeOfAnUnknownEntryAndShowsAZeroFolderAsZeroBytes()
    {
        using var context = new TestContext();
        context.Catalog.Save("empty-shelf", new CatalogDocument
        {
            Id = "empty-shelf",
            Name = "Empty shelf",
            ParentId = "movies",
            Type = CatalogDocumentType.Folder
        });
        context.Catalog.Save("unmeasured", new CatalogDocument
        {
            Id = "unmeasured",
            Name = "Unmeasured.flac",
            ParentId = "music",
            Type = CatalogDocumentType.File,
            Properties = new PropertiesCollection { [CatalogDocument.SizeKnownPropertyKey] = false }
        });
        using var viewModel = context.CreateViewModel();

        SelectTreeNode(viewModel, "movies");
        Assert.Equal("0 b", viewModel.BrowserItems.Single(item => item.Id == "empty-shelf").DisplaySize);

        SelectTreeNode(viewModel, "music");
        Assert.Equal(string.Empty, viewModel.BrowserItems.Single(item => item.Id == "unmeasured").DisplaySize);
        Assert.Equal(SizeFormatting.FormatBytes(512, "0.##"), viewModel.BrowserItems.Single(item => item.Id == "track-1").DisplaySize);
    }

    private static BrowserTreeNode SelectTreeNode(MainWindowViewModel viewModel, string key)
    {
        var node = Flatten(viewModel.TreeNodes).FirstOrDefault(candidate => candidate.Key == key);
        Assert.NotNull(node);
        viewModel.SelectedTreeNode = node;
        return node!;
    }

    private static PropertiesDialogViewModel OpenProperties(MainWindowViewModel viewModel, string itemId)
    {
        viewModel.ActivePane = ActivePane.Browser;
        viewModel.SelectedBrowserItem = viewModel.BrowserItems.Single(item => item.Id == itemId);
        PropertiesDialogRequestedEventArgs? request = null;
        void OnRequested(object? sender, PropertiesDialogRequestedEventArgs args) => request = args;
        viewModel.PropertiesRequested += OnRequested;
        try
        {
            viewModel.OpenPropertiesCommand.Execute(null);
        }
        finally
        {
            viewModel.PropertiesRequested -= OnRequested;
        }

        Assert.NotNull(request);
        return request!.Dialog;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for background size maintenance: {because}.");
            }

            await Task.Delay(50);
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

    /// <summary>Owns the container, database and repositories for one test, and the catalog they hold.</summary>
    private sealed class TestContext : IDisposable
    {
        private readonly Container container = new();
        private readonly RepositoryManager repositories;

        public TestContext()
        {
            new CouchbaseServiceRegistrator().RegisterServices(container);
            repositories = container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
            SeedCatalog();
        }

        public CatalogDocumentRepository Catalog { get; }

        public MainWindowViewModel CreateViewModel() => new(repositories);

        private void SeedCatalog()
        {
            Save(new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder, Size = 6144 + 512 });
            Save(new CatalogDocument { Id = "movies", Name = "Movies", ParentId = "library", Type = CatalogDocumentType.Folder, Size = 6144 });
            Save(new CatalogDocument { Id = "music", Name = "Music", ParentId = "library", Type = CatalogDocumentType.Folder, Size = 512 });
            Save(new CatalogDocument { Id = "scifi", Name = "Sci-Fi", ParentId = "movies", Type = CatalogDocumentType.Folder, Size = 6144 });

            Save(new CatalogDocument
            {
                Id = "interstellar",
                Name = "Interstellar.mkv",
                ParentId = "scifi",
                Type = CatalogDocumentType.File,
                Size = 4096
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
                Id = "track-1",
                Name = "track-01.flac",
                ParentId = "music",
                Type = CatalogDocumentType.File,
                Size = 512
            });
        }

        private void Save(CatalogDocument document) => Catalog.Save(document.Id, document);

        public void Dispose()
        {
            try
            {
                container.Resolve<DatabaseManager>().GetDatabase().Close();
            }
            catch (Exception exception) when (exception is CouchbaseException or InvalidOperationException)
            {
                // Already closed by the test.
            }

            container.Dispose();
        }
    }
}
