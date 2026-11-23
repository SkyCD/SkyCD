using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

public class BrowserItemRenameTests
{
    [Fact]
    public void BrowserContextMenu_OffersRenameForTheListPane()
    {
        using var vm = CreateViewModel();

        Assert.Contains(
            vm.BrowserContextMenuItems,
            item => ReferenceEquals(item.Command, vm.RenameItemCommand) &&
                    Equals(item.CommandParameter, "list"));
    }

    [Fact]
    public void RenameItemCommand_StartsInlineEditingOnTheSelectedBrowserItem()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");
        var requested = 0;
        vm.RenameRequested += (_, _) => requested++;

        Assert.True(vm.RenameItemCommand.CanExecute("list"));
        vm.RenameItemCommand.Execute("list");

        Assert.Equal(item.Id, vm.EditingBrowserItemId);
        Assert.Equal("Interstellar.mkv", vm.BrowserItemRenameText);
        Assert.Null(vm.BrowserItemRenameError);
        Assert.False(vm.HasBrowserItemRenameError);
        Assert.Equal(1, requested);
        Assert.Same(item, vm.SelectedBrowserItem);
        Assert.DoesNotContain(vm.TreeNodes.SelectMany(Flatten), node => node.IsRenaming);
    }

    [Fact]
    public void RenameItemCommand_RequiresExactlyOneSelectedBrowserItem()
    {
        using var vm = CreateViewModel();
        var interstellar = vm.BrowserItems.Single(item => item.Id == "interstellar");
        var arrival = vm.BrowserItems.Single(item => item.Id == "arrival");

        vm.SelectedBrowserItems = new List<CatalogDocument> { interstellar, arrival };

        Assert.False(vm.RenameItemCommand.CanExecute("list"));
        Assert.False(vm.CanRenameBrowserItem(interstellar));
        Assert.True(vm.RenameItemCommand.CanExecute("tree"));

        vm.BeginBrowserItemRename(interstellar);
        Assert.Null(vm.EditingBrowserItemId);
    }

    [Fact]
    public void CommitBrowserItemRename_PersistsTheNameAndRefreshesTheList()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");
        var focusRequests = 0;
        vm.FocusBrowserRequested += (_, _) => focusRequests++;

        vm.BeginBrowserItemRename(item);
        vm.BrowserItemRenameText = "  Interstellar (2014).mkv  ";
        vm.CommitBrowserItemRename();

        Assert.Null(vm.EditingBrowserItemId);
        Assert.Equal(string.Empty, vm.BrowserItemRenameText);
        Assert.Null(vm.BrowserItemRenameError);
        Assert.True(vm.IsDirtyDocument);
        Assert.Equal("Renamed to Interstellar (2014).mkv.", vm.StatusText);
        Assert.Contains(vm.BrowserItems, browserItem => browserItem.Id == "interstellar" && browserItem.Name == "Interstellar (2014).mkv");
        Assert.DoesNotContain(vm.BrowserItems, browserItem => browserItem.Id == "interstellar" && browserItem.Name == "Interstellar.mkv");
        Assert.Equal("interstellar", vm.SelectedBrowserItem?.Id);
        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public void CommitBrowserItemRename_KeepsEditingAndExplainsAnEmptyName()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");

        vm.BeginBrowserItemRename(item);
        vm.BrowserItemRenameText = "   ";
        vm.CommitBrowserItemRename();

        Assert.Equal(item.Id, vm.EditingBrowserItemId);
        Assert.True(vm.HasBrowserItemRenameError);
        Assert.Equal("Enter a name.", vm.BrowserItemRenameError);
        Assert.Equal("Enter a name.", vm.StatusText);
        Assert.False(vm.IsDirtyDocument);
        Assert.Contains(vm.BrowserItems, browserItem => browserItem.Id == "interstellar" && browserItem.Name == "Interstellar.mkv");
    }

    [Fact]
    public void CommitBrowserItemRename_SameNameLeavesStateUnchanged()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");
        var focusRequests = 0;
        vm.FocusBrowserRequested += (_, _) => focusRequests++;

        vm.BeginBrowserItemRename(item);
        vm.BrowserItemRenameText = "Interstellar.mkv";
        vm.CommitBrowserItemRename();

        Assert.Null(vm.EditingBrowserItemId);
        Assert.False(vm.IsDirtyDocument);
        Assert.Contains(vm.BrowserItems, browserItem => browserItem.Id == "interstellar" && browserItem.Name == "Interstellar.mkv");
        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public void CancelBrowserItemRename_DiscardsTheEditAndKeepsTheOriginalName()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");
        var focusRequests = 0;
        vm.FocusBrowserRequested += (_, _) => focusRequests++;

        vm.BeginBrowserItemRename(item);
        vm.BrowserItemRenameText = "Draft name";
        vm.CancelBrowserItemRename();

        Assert.Null(vm.EditingBrowserItemId);
        Assert.Equal(string.Empty, vm.BrowserItemRenameText);
        Assert.Null(vm.BrowserItemRenameError);
        Assert.False(vm.IsDirtyDocument);
        Assert.Contains(vm.BrowserItems, browserItem => browserItem.Id == "interstellar" && browserItem.Name == "Interstellar.mkv");
        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public void CancelBrowserItemRename_WithoutFocusRestore_DoesNotStealFocus()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");
        var focusRequests = 0;
        vm.FocusBrowserRequested += (_, _) => focusRequests++;

        vm.BeginBrowserItemRename(item);
        vm.CancelBrowserItemRename(restoreFocus: false);

        Assert.Null(vm.EditingBrowserItemId);
        Assert.Equal(0, focusRequests);
    }

    [Fact]
    public void SelectingAnotherTreeNode_CancelsTheBrowserItemEdit()
    {
        using var vm = CreateViewModel();
        var item = SelectItem(vm, "interstellar");

        vm.BeginBrowserItemRename(item);
        vm.SelectedTreeNode = FindNode(vm, "library");

        Assert.Null(vm.EditingBrowserItemId);
        Assert.Equal(string.Empty, vm.BrowserItemRenameText);
    }

    [Fact]
    public void BeginningOneSurfaceEdit_CancelsTheOtherSurfaceEdit()
    {
        using var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        var item = SelectItem(vm, "interstellar");

        vm.BeginBrowserItemRename(item);
        vm.BeginRename(movies);

        Assert.Null(vm.EditingBrowserItemId);
        Assert.True(movies.IsRenaming);

        vm.BeginBrowserItemRename(item);
        Assert.False(movies.IsRenaming);
        Assert.Equal(item.Id, vm.EditingBrowserItemId);
    }

    private static CatalogDocument SelectItem(MainWindowViewModel vm, string id)
    {
        var item = vm.BrowserItems.Single(browserItem => browserItem.Id == id);
        vm.SelectedBrowserItem = item;
        return item;
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        vm.SelectedTreeNode = FindNode(vm, "movies");
        return vm;
    }

    internal static BrowserTreeNode FindNode(MainWindowViewModel vm, string key) =>
        vm.TreeNodes.SelectMany(Flatten).Single(node => node.Key == key);

    internal static IEnumerable<BrowserTreeNode> Flatten(BrowserTreeNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}

/// <summary>
/// Runs the rename path against the real catalog repository. The projected case seeds the runtime
/// overlay the view model uses for entries a plugin projects but does not store.
/// </summary>
public sealed class BrowserItemRenamePersistenceTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [Fact]
    public async Task CommitBrowserItemRename_PersistsStoredEntryAndIsUndoable()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        foreach (var entry in TestCatalogEntries.Default())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        using var vm = new MainWindowViewModel(repositories);
        vm.SelectedTreeNode = BrowserItemRenameTests.FindNode(vm, "movies");
        var item = vm.BrowserItems.Single(browserItem => browserItem.Id == "interstellar");
        vm.SelectedBrowserItem = item;

        vm.BeginBrowserItemRename(item);
        vm.BrowserItemRenameText = "Interstellar (2014).mkv";
        vm.CommitBrowserItemRename();

        Assert.Null(vm.EditingBrowserItemId);
        Assert.Equal("Interstellar (2014).mkv", catalogRepository.Get("interstellar")!.Name);
        Assert.True(vm.IsDirtyDocument);
        Assert.Contains(vm.BrowserItems, browserItem => browserItem.Id == "interstellar" && browserItem.Name == "Interstellar (2014).mkv");
        Assert.NotNull(vm.UndoManager);
        Assert.True(vm.UndoManager!.CanUndo);

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Interstellar.mkv", catalogRepository.Get("interstellar")!.Name);
    }

    [Fact]
    public void CommitBrowserItemRename_StagesProjectedEntryWithoutPersisting()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var catalogRepository = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        foreach (var entry in TestCatalogEntries.Default())
        {
            catalogRepository.Save(entry.Id, entry);
        }

        using var vm = new MainWindowViewModel(repositories);
        SeedProjectedEntry(vm, new CatalogDocument
        {
            Id = "projected-entry",
            Name = "Projected Entry.mkv",
            ParentId = "movies",
            Type = CatalogDocumentType.Media
        });
        vm.SelectedTreeNode = BrowserItemRenameTests.FindNode(vm, "library");
        vm.SelectedTreeNode = BrowserItemRenameTests.FindNode(vm, "movies");
        var item = vm.BrowserItems.Single(browserItem => browserItem.Id == "projected-entry");
        vm.SelectedBrowserItem = item;

        vm.BeginBrowserItemRename(item);
        vm.BrowserItemRenameText = "Projected Entry (renamed).mkv";
        vm.CommitBrowserItemRename();

        Assert.Null(vm.EditingBrowserItemId);
        Assert.Null(catalogRepository.Get("projected-entry"));
        Assert.Contains(vm.BrowserItems, browserItem => browserItem.Id == "projected-entry" && browserItem.Name == "Projected Entry (renamed).mkv");
        Assert.True(vm.IsDirtyDocument);
        Assert.False(vm.UndoManager!.CanUndo);
    }

    private static void SeedProjectedEntry(MainWindowViewModel vm, CatalogDocument entry)
    {
        var field = typeof(MainWindowViewModel).GetField(
            "addedItemsByNodeKey",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var runtimeItems = Assert.IsType<Dictionary<string, List<CatalogDocument>>>(field!.GetValue(vm));
        runtimeItems[entry.ParentId ?? "library"] = [entry];
    }
}
