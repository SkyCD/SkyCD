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
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CollectionMenuStartupTests : IDisposable
{
    private const string RootId = "disk";
    private const string FolderId = "folder-4c44629afe714394a28aa9c90dbe6505";

    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    private sealed class Stack : IDisposable
    {
        public Stack()
        {
            Container = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(Container);
            Repositories = Container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)Repositories.For<CatalogDocument>();
            Collections = (VirtualCollectionDocumentRepository)Repositories.For<VirtualCollectionDocument>();
        }

        public Container Container { get; }

        public RepositoryManager Repositories { get; }

        public CatalogDocumentRepository Catalog { get; }

        public VirtualCollectionDocumentRepository Collections { get; }

        public MainWindowViewModel CreateViewModel() => new(Repositories);

        public void Dispose() => Container.Dispose();
    }

    private static CatalogDocument Folder(string id, string? parentId) => new()
    {
        Id = id,
        Name = id,
        ParentId = parentId,
        Type = CatalogDocumentType.Folder,
        ChildrenCount = 0
    };

    private static CatalogDocument File(string id, string parentId) => new()
    {
        Id = id,
        Name = id,
        ParentId = parentId,
        Type = CatalogDocumentType.File,
        Size = 100,
        ChildrenCount = 0
    };

    private static Stack CreateSeededStack()
    {
        var stack = new Stack();
        stack.Catalog.Save(RootId, Folder(RootId, null));
        stack.Catalog.Save(FolderId, Folder(FolderId, RootId));
        stack.Catalog.Save("file-a", File("file-a", FolderId));
        stack.Catalog.Save("file-b", File("file-b", FolderId));
        var optionsRepository = (AppOptionsDocumentRepository)stack.Repositories.For<AppOptionsDocument>();
        var options = optionsRepository.GetOrCreateAppOptions();
        options.AppStartCount = 1;
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);
        return stack;
    }

    private static VirtualCollectionDocument CreateCollection(
        Stack stack,
        string name,
        params string[] entryIds)
    {
        var collection = stack.Collections.CreateCollection(name);
        collection.EntryIds = entryIds.ToList();
        stack.Collections.Save(collection.Id, collection);
        return collection;
    }

    private static MainMenuItemViewModel CollectionsParent(MainWindowViewModel viewModel) =>
        viewModel.BrowserContextMenuItems.Single(item => item.Key == "edit_collections");

    private static MainMenuItemViewModel CollectionItem(MainWindowViewModel viewModel, string collectionId) =>
        CollectionsParent(viewModel).Items.Single(item =>
            string.Equals(item.CommandParameter as string, collectionId, StringComparison.Ordinal));

    private static void SelectFolder(MainWindowViewModel viewModel)
    {
        viewModel.SelectedTreeNode = viewModel.TreeNodes
            .Single()
            .Children.Single(child => child.Key == FolderId);
    }

    private static void Select(MainWindowViewModel viewModel, params string[] entryIds)
    {
        var items = entryIds
            .Select(id => viewModel.BrowserItems.Single(item => item.Id == id))
            .ToList();
        viewModel.SelectedBrowserItem = items[0];
        viewModel.SelectedBrowserItems = items;
    }

    private static IReadOnlyList<string> StoredEntryIds(Stack stack, string collectionId) =>
        stack.Collections.Get(collectionId)!.EntryIds ?? [];

    [Fact]
    public void StartupWithStaticCollectionAndSelectedFolder_DoesNotThrowAndShowsCorrectState()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites", "file-a");

        using var viewModel = stack.CreateViewModel();
        Select(viewModel, FolderId);

        var item = CollectionItem(viewModel, collection.Id);
        Assert.True(item.IsEnabled);
        Assert.Equal("☐", item.Icon);
        Assert.Equal(["file-a"], StoredEntryIds(stack, collection.Id));
    }

    [Fact]
    public void CollectionMenu_ReportsUncheckedCheckedAndPartialStates()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites", "file-a");
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);

        Select(viewModel, "file-b");
        Assert.Equal("☐", CollectionItem(viewModel, collection.Id).Icon);

        Select(viewModel, "file-a", "file-b");
        Assert.Equal("◐", CollectionItem(viewModel, collection.Id).Icon);

        Select(viewModel, "file-a");
        Assert.Equal("☑", CollectionItem(viewModel, collection.Id).Icon);

        viewModel.SelectedBrowserItems = new List<CatalogDocument>();
        viewModel.SelectedBrowserItem = null;
        var emptyItem = CollectionItem(viewModel, collection.Id);
        Assert.Equal("☐", emptyItem.Icon);
        Assert.False(emptyItem.IsEnabled);
    }

    [Fact]
    public void Toggle_AddsOnlyMissingSelectedEntriesAndKeepsUnrelatedMembers()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites", "file-b", "kept-elsewhere");
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);
        Select(viewModel, "file-a", "file-b");

        CollectionItem(viewModel, collection.Id).Command!.Execute(collection.Id);

        Assert.Equal(["file-a", "file-b", "kept-elsewhere"], StoredEntryIds(stack, collection.Id));
        Assert.Equal("☑", CollectionItem(viewModel, collection.Id).Icon);
        Assert.NotNull(stack.Catalog.Get("file-a"));
        Assert.Equal(FolderId, stack.Catalog.Get("file-a")!.ParentId);
    }

    [Fact]
    public void Toggle_RemovesAllSelectedEntriesWhenAllAreMembers()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites", "file-a", "file-b", "kept-elsewhere");
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);
        Select(viewModel, "file-a", "file-b");

        CollectionItem(viewModel, collection.Id).Command!.Execute(collection.Id);

        Assert.Equal(["kept-elsewhere"], StoredEntryIds(stack, collection.Id));
        Assert.Equal("☐", CollectionItem(viewModel, collection.Id).Icon);
        Assert.NotNull(stack.Catalog.Get("file-a"));
        Assert.NotNull(stack.Catalog.Get("file-b"));
    }

    [Fact]
    public async Task Toggle_MultiSelectionSupportsUndoAndRedo()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites", "file-b");
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);
        Select(viewModel, "file-a", "file-b");

        CollectionItem(viewModel, collection.Id).Command!.Execute(collection.Id);
        Assert.Equal(["file-a", "file-b"], StoredEntryIds(stack, collection.Id));

        await viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal(["file-b"], StoredEntryIds(stack, collection.Id));
        SelectFolder(viewModel);
        Select(viewModel, "file-a", "file-b");
        Assert.Equal("◐", CollectionItem(viewModel, collection.Id).Icon);

        await viewModel.RedoCommand.ExecuteAsync(null);
        Assert.Equal(["file-a", "file-b"], StoredEntryIds(stack, collection.Id));
        Assert.Equal("☑", CollectionItem(viewModel, collection.Id).Icon);
    }

    [Fact]
    public void Toggle_HandlesEntryIdCollidingWithCollectionId()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites");
        stack.Catalog.Save(collection.Id, File(collection.Id, FolderId));
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);
        Select(viewModel, collection.Id, "file-a");

        var item = CollectionItem(viewModel, collection.Id);
        Assert.Equal("☐", item.Icon);

        item.Command!.Execute(collection.Id);

        Assert.Equal([collection.Id, "file-a"], StoredEntryIds(stack, collection.Id).OrderBy(static id => id, StringComparer.Ordinal));
        Assert.Equal("☑", CollectionItem(viewModel, collection.Id).Icon);
    }

    [Fact]
    public void StartupAndSelection_SurviveMissingEntryReferencesInCollection()
    {
        using var stack = CreateSeededStack();
        var collection = CreateCollection(stack, "Favorites", "deleted-entry");
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);
        Select(viewModel, "file-a");

        Assert.Equal("☐", CollectionItem(viewModel, collection.Id).Icon);
        Assert.Equal(["deleted-entry"], StoredEntryIds(stack, collection.Id));
    }

    [Fact]
    public void Restart_WithPersistedCollectionAndSelection_RemainsUsable()
    {
        string collectionId;
        using (var stack = CreateSeededStack())
        {
            collectionId = CreateCollection(stack, "Favorites", "file-a").Id;
            using var first = stack.CreateViewModel();
            SelectFolder(first);
            Select(first, "file-a", "file-b");
            CollectionItem(first, collectionId).Command!.Execute(collectionId);
        }

        using var reopened = new Stack();
        using var second = reopened.CreateViewModel();
        SelectFolder(second);
        Assert.Equal("☑", CollectionItem(second, collectionId).Icon);
        Assert.Equal(["file-a", "file-b"], StoredEntryIds(reopened, collectionId));
        Assert.NotNull(reopened.Catalog.Get("file-a"));
        Assert.Equal(FolderId, reopened.Catalog.Get("file-b")!.ParentId);
    }

    [Fact]
    public void UnknownCollection_StillThrowsInsteadOfBeingSilentlyIgnored()
    {
        using var stack = CreateSeededStack();
        using var viewModel = stack.CreateViewModel();
        SelectFolder(viewModel);
        Select(viewModel, "file-a");

        Assert.Throws<SkyCD.Documents.CollectionNotFoundException>(() =>
            new SkyCD.Presentation.Collections.VirtualCollectionService(
                stack.Catalog,
                stack.Collections).GetEntryIds("missing-collection"));
    }
}
