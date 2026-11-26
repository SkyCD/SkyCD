using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Collections;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.UndoRedo;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CollectionsDialogViewModelTests : IDisposable
{
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
            Catalog.Save("root", new CatalogDocument
            {
                Id = "root",
                Name = "Root",
                ParentId = null,
                Type = CatalogDocumentType.Folder,
                ChildrenCount = 1
            });
            Catalog.Save("file-a", new CatalogDocument
            {
                Id = "file-a",
                Name = "File A",
                ParentId = "root",
                Type = CatalogDocumentType.File,
                Size = 10,
                ChildrenCount = 0
            });
            Service = new VirtualCollectionService(Catalog, Collections);
            Undo = new CatalogUndoManager(Catalog);
        }

        public Container Container { get; }

        public RepositoryManager Repositories { get; }

        public CatalogDocumentRepository Catalog { get; }

        public VirtualCollectionDocumentRepository Collections { get; }

        public VirtualCollectionService Service { get; }

        public CatalogUndoManager Undo { get; }

        public CollectionsDialogViewModel CreateViewModel(
            Action? onChanged = null,
            Action<CatalogDocument>? navigate = null,
            Func<IReadOnlyList<string>>? selectedEntryIdsProvider = null) =>
            new(Service, Undo, Catalog, Collections, onChanged, navigate, selectedEntryIdsProvider);

        public VirtualCollectionDocument CreateCollection(string name, params string[] entryIds)
        {
            var collection = Collections.CreateCollection(name);
            collection.EntryIds = entryIds.ToList();
            Collections.Save(collection.Id, collection);
            return collection;
        }

        public void Dispose() => Container.Dispose();
    }

    [Fact]
    public void CreateCollection_AddsSelectsAndNotifies()
    {
        using var stack = new Stack();
        var changes = 0;
        var viewModel = stack.CreateViewModel(onChanged: () => changes++);

        viewModel.NewCollectionName = "  Favorites  ";
        viewModel.CreateCollectionCommand.Execute(null);

        var created = Assert.Single(viewModel.Collections);
        Assert.Equal("Favorites", created.Name);
        Assert.Same(created, viewModel.SelectedCollection);
        Assert.Equal(1, changes);
        Assert.False(viewModel.ShowEmptyCollectionsState);
        Assert.Equal("Favorites", stack.Collections.FindByName("Favorites")!.Name);
        Assert.True(viewModel.DeleteCollectionCommand.CanExecute(null));
    }

    [Fact]
    public void CreateCollection_WithoutName_KeepsListUnchanged()
    {
        using var stack = new Stack();
        var viewModel = stack.CreateViewModel();

        viewModel.NewCollectionName = "   ";
        viewModel.CreateCollectionCommand.Execute(null);

        Assert.Empty(viewModel.Collections);
        Assert.True(viewModel.ShowEmptyCollectionsState);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));
    }

    [Fact]
    public void RenameCollection_PersistsTheEditedName()
    {
        using var stack = new Stack();
        stack.CreateCollection("Old name");
        var viewModel = stack.CreateViewModel();
        Assert.Equal("Old name", viewModel.RenameTo);

        viewModel.RenameTo = "  New name  ";
        viewModel.RenameCollectionCommand.Execute(null);

        Assert.Equal("New name", Assert.Single(viewModel.Collections).Name);
        Assert.Equal("New name", stack.Collections.GetOrdered().Single().Name);
    }

    [Fact]
    public void RenameCollection_RequiresSelection()
    {
        using var stack = new Stack();
        var viewModel = stack.CreateViewModel();

        Assert.False(viewModel.RenameCollectionCommand.CanExecute(null));
        Assert.False(viewModel.DeleteCollectionCommand.CanExecute(null));

        viewModel.NewCollectionName = "Selection";
        viewModel.CreateCollectionCommand.Execute(null);

        Assert.True(viewModel.RenameCollectionCommand.CanExecute(null));
        Assert.True(viewModel.DeleteCollectionCommand.CanExecute(null));
    }

    [Fact]
    public void DeleteCollection_RemovesSelectionAndNotifies()
    {
        using var stack = new Stack();
        stack.CreateCollection("Temporary");
        var changes = 0;
        var viewModel = stack.CreateViewModel(onChanged: () => changes++);

        viewModel.DeleteCollectionCommand.Execute(null);
        Assert.True(viewModel.IsDeleteCollectionConfirmationVisible);
        Assert.Contains("Temporary", viewModel.DeleteCollectionConfirmationPrompt, StringComparison.Ordinal);

        viewModel.ConfirmDeleteCollectionCommand.Execute(null);

        Assert.False(viewModel.IsDeleteCollectionConfirmationVisible);
        Assert.Empty(viewModel.Collections);
        Assert.Empty(stack.Collections.GetOrdered());
        Assert.Equal(1, changes);
        Assert.True(stack.Undo.CanUndo);
    }

    [Fact]
    public void DeleteCollection_CancelKeepsTheCollection()
    {
        using var stack = new Stack();
        stack.CreateCollection("Temporary");
        var viewModel = stack.CreateViewModel();

        viewModel.DeleteCollectionCommand.Execute(null);
        viewModel.CancelDeleteCollectionCommand.Execute(null);

        Assert.False(viewModel.IsDeleteCollectionConfirmationVisible);
        Assert.Single(viewModel.Collections);
        Assert.False(stack.Undo.CanUndo);
    }

    [Fact]
    public void RemoveMissingEntries_PrunesDeletedReferences()
    {
        using var stack = new Stack();
        stack.CreateCollection("Mixed", "file-a", "missing-entry");
        var viewModel = stack.CreateViewModel();

        Assert.True(viewModel.HasMissingMembers);
        Assert.Equal(1, viewModel.UnavailableCount);
        Assert.Equal("File A", Assert.Single(viewModel.Members).Name);
        Assert.True(viewModel.RemoveMissingEntriesCommand.CanExecute(null));

        viewModel.RemoveMissingEntriesCommand.Execute(null);

        Assert.False(viewModel.HasMissingMembers);
        Assert.Equal(["file-a"], stack.Collections.GetOrdered().Single().EntryIds);
        Assert.Single(viewModel.Members);
    }

    [Fact]
    public void NavigateToSelected_OpensTheResolvedEntry()
    {
        using var stack = new Stack();
        stack.CreateCollection("Navigable", "file-a");
        CatalogDocument? navigated = null;
        var viewModel = stack.CreateViewModel(navigate: document => navigated = document);

        viewModel.SelectedMember = Assert.Single(viewModel.Members);
        Assert.True(viewModel.NavigateToSelectedCommand.CanExecute(null));

        viewModel.NavigateToSelectedCommand.Execute(null);

        Assert.Equal("file-a", navigated?.Id);
    }

    [Fact]
    public void SelectingCollection_RefreshesMembersAndRenameTarget()
    {
        using var stack = new Stack();
        stack.CreateCollection("Alpha", "file-a");
        stack.CreateCollection("Beta");
        var viewModel = stack.CreateViewModel();
        var alpha = viewModel.Collections.Single(item => item.Name == "Alpha");
        var beta = viewModel.Collections.Single(item => item.Name == "Beta");

        viewModel.SelectedCollection = beta;
        Assert.Equal("Beta", viewModel.RenameTo);
        Assert.Empty(viewModel.Members);

        viewModel.SelectedCollection = alpha;
        Assert.Equal("Alpha", viewModel.RenameTo);
        Assert.Single(viewModel.Members);
    }

    [Fact]
    public void OptionsPage_HostsCollectionsAndTheMenuCommandPreselectsIt()
    {
        using var stack = new Stack();
        stack.CreateCollection("From menu");
        using var mainViewModel = new MainWindowViewModel(stack.Repositories);
        OptionsDialogRequestedEventArgs? request = null;
        mainViewModel.OptionsRequested += (_, args) => request = args;

        mainViewModel.OpenCollectionsDialogCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, request!.PreselectCategoryId);
        var page = request.Dialog.CollectionsPage;
        Assert.NotNull(page);
        Assert.Equal(["From menu"], page!.Collections.Select(item => item.Name));
    }

    [Fact]
    public void InlineRename_CommitsEditedName()
    {
        using var stack = new Stack();
        stack.CreateCollection("Old name", "file-a");
        var viewModel = stack.CreateViewModel();
        var item = Assert.Single(viewModel.Collections);

        viewModel.BeginRenameCollectionCommand.Execute(item);
        Assert.True(item.IsRenaming);
        Assert.Equal("Old name", item.RenameDraft);

        item.RenameDraft = "  New name  ";
        viewModel.CommitRenameCollectionCommand.Execute(item);

        Assert.False(item.IsRenaming);
        Assert.Equal("New name", viewModel.Collections.Single().Name);
        Assert.Equal("New name", stack.Collections.GetOrdered().Single().Name);
        Assert.True(stack.Undo.CanUndo);
    }

    [Fact]
    public void InlineRename_CancelDiscardsDraft()
    {
        using var stack = new Stack();
        stack.CreateCollection("Old name");
        var viewModel = stack.CreateViewModel();
        var item = Assert.Single(viewModel.Collections);

        viewModel.BeginRenameCollectionCommand.Execute(item);
        item.RenameDraft = "Abandoned";
        viewModel.CancelRenameCollectionCommand.Execute(item);

        Assert.False(item.IsRenaming);
        Assert.Equal("Old name", viewModel.Collections.Single().Name);
        Assert.False(stack.Undo.CanUndo);
    }

    [Fact]
    public void InlineRename_DuplicateKeepsEditOpen()
    {
        using var stack = new Stack();
        stack.CreateCollection("Alpha");
        stack.CreateCollection("Beta");
        var viewModel = stack.CreateViewModel();
        var beta = viewModel.Collections.Single(item => item.Name == "Beta");

        viewModel.BeginRenameCollectionCommand.Execute(beta);
        beta.RenameDraft = "Alpha";
        viewModel.CommitRenameCollectionCommand.Execute(beta);

        Assert.True(beta.IsRenaming);
        Assert.Equal("Beta", stack.Collections.FindByName("Beta")!.Name);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));
    }

    [Fact]
    public void AddSelectedEntries_AddsBrowserSelection()
    {
        using var stack = new Stack();
        stack.CreateCollection("Manual");
        var viewModel = stack.CreateViewModel(selectedEntryIdsProvider: () => ["file-a"]);

        Assert.True(viewModel.AddSelectedEntriesCommand.CanExecute(null));

        viewModel.AddSelectedEntriesCommand.Execute(null);

        Assert.Equal(["file-a"], stack.Service.GetEntryIds(viewModel.Collections.Single().Id));
        Assert.Equal("file-a", Assert.Single(viewModel.Members).Id);
        Assert.Contains("Manual", viewModel.StatusText, StringComparison.Ordinal);
        Assert.True(stack.Undo.CanUndo);
    }

    [Fact]
    public void AddSelectedEntries_WithoutSelectionExplains()
    {
        using var stack = new Stack();
        stack.CreateCollection("Manual");
        var viewModel = stack.CreateViewModel(selectedEntryIdsProvider: () => []);

        viewModel.AddSelectedEntriesCommand.Execute(null);

        Assert.Empty(stack.Service.GetEntryIds(viewModel.Collections.Single().Id));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));
    }

    [Fact]
    public void AddSelectedEntries_IsDisabledForDynamicCollections()
    {
        using var stack = new Stack();
        stack.Service.CreateDynamicCollection("Live", NameContainsQuery("File"));
        var viewModel = stack.CreateViewModel(selectedEntryIdsProvider: () => ["file-a"]);

        Assert.False(viewModel.AddSelectedEntriesCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveMember_AsksForConfirmationThenRemoves()
    {
        using var stack = new Stack();
        stack.CreateCollection("Mixed", "file-a");
        var viewModel = stack.CreateViewModel();
        viewModel.SelectedMember = Assert.Single(viewModel.Members);

        Assert.True(viewModel.RequestRemoveSelectedMemberCommand.CanExecute(null));
        Assert.False(viewModel.IsRemoveMemberConfirmationVisible);

        viewModel.RequestRemoveSelectedMemberCommand.Execute(null);

        Assert.True(viewModel.IsRemoveMemberConfirmationVisible);
        Assert.Contains("File A", viewModel.RemoveMemberConfirmationPrompt, StringComparison.Ordinal);

        viewModel.ConfirmRemoveSelectedMemberCommand.Execute(null);

        Assert.False(viewModel.IsRemoveMemberConfirmationVisible);
        Assert.Empty(viewModel.Members);
        Assert.Empty(stack.Service.GetEntryIds(viewModel.Collections.Single().Id));
        Assert.Contains("File A", viewModel.StatusText, StringComparison.Ordinal);
        Assert.True(stack.Undo.CanUndo);
    }

    [Fact]
    public void RemoveMember_CancelKeepsTheMember()
    {
        using var stack = new Stack();
        stack.CreateCollection("Mixed", "file-a");
        var viewModel = stack.CreateViewModel();
        viewModel.SelectedMember = Assert.Single(viewModel.Members);

        viewModel.RequestRemoveSelectedMemberCommand.Execute(null);
        viewModel.CancelRemoveMemberCommand.Execute(null);

        Assert.False(viewModel.IsRemoveMemberConfirmationVisible);
        Assert.Single(viewModel.Members);
    }

    [Fact]
    public void RemoveMember_IsDisabledForDynamicCollections()
    {
        using var stack = new Stack();
        stack.Service.CreateDynamicCollection("Live", NameContainsQuery("File"));
        var viewModel = stack.CreateViewModel();
        viewModel.SelectedMember = Assert.Single(viewModel.Members);

        Assert.False(viewModel.RequestRemoveSelectedMemberCommand.CanExecute(null));
    }

    [Fact]
    public void OpenMemberFolder_NavigatesToTheParent()
    {
        using var stack = new Stack();
        stack.CreateCollection("Mixed", "file-a");
        CatalogDocument? navigated = null;
        var viewModel = stack.CreateViewModel(navigate: document => navigated = document);
        viewModel.SelectedMember = Assert.Single(viewModel.Members);

        viewModel.OpenMemberFolderCommand.Execute(null);

        Assert.Equal("root", navigated?.Id);
    }

    [Fact]
    public void MemberFilter_NarrowsTheVisibleMembers()
    {
        using var stack = new Stack();
        stack.Catalog.Save("file-b", new CatalogDocument
        {
            Id = "file-b",
            Name = "File B",
            ParentId = "root",
            Type = CatalogDocumentType.File,
            Size = 5,
            ChildrenCount = 0
        });
        stack.CreateCollection("Mixed", "file-a", "file-b");
        var viewModel = stack.CreateViewModel();
        Assert.Equal(2, viewModel.FilteredMembers.Count);

        viewModel.MemberFilterText = "file b";

        var filtered = Assert.Single(viewModel.FilteredMembers);
        Assert.Equal("file-b", filtered.Id);

        viewModel.MemberFilterText = string.Empty;

        Assert.Equal(2, viewModel.FilteredMembers.Count);
    }

    [Fact]
    public void SavedQuery_SelectionShowsDescriptionAndCountsPredicates()
    {
        using var stack = new Stack();
        stack.Service.CreateDynamicCollection("Live", NameContainsQuery("File"));
        var viewModel = stack.CreateViewModel();

        Assert.True(viewModel.IsDynamicSelected);
        Assert.NotNull(viewModel.SavedQueryDescription);
        Assert.Contains("1", viewModel.SavedQueryDescription, StringComparison.Ordinal);
        Assert.False(viewModel.IsMissingSavedQuery);
    }

    [Fact]
    public void SavedQuery_MissingQueryShowsDedicatedState()
    {
        using var stack = new Stack();
        var collection = stack.Collections.CreateCollection("Broken");
        collection.Kind = VirtualCollectionKind.Dynamic;
        collection.SavedQueryJson = null;
        stack.Collections.Save(collection.Id, collection);
        var viewModel = stack.CreateViewModel();

        Assert.True(viewModel.IsDynamicSelected);
        Assert.True(viewModel.IsMissingSavedQuery);
        Assert.Null(viewModel.SavedQueryDescription);
        Assert.Empty(viewModel.FilteredMembers);
    }

    [Fact]
    public void ManualCollection_ShowsEntryAndMissingSummary()
    {
        using var stack = new Stack();
        stack.CreateCollection("Mixed", "file-a", "missing-entry");
        var viewModel = stack.CreateViewModel();

        Assert.True(viewModel.HasMissingMembers);
        Assert.Equal(1, viewModel.UnavailableCount);
        var item = Assert.Single(viewModel.Collections);
        Assert.Contains("1", item.EntrySummary, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.MissingMembersNotice));
    }

    private static AdvancedCatalogQuery NameContainsQuery(string text) =>
        MainWindowViewModel.BuildNameContainsQuery(text);
}
