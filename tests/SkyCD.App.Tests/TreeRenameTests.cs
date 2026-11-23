using System.Collections.Generic;
using System.Linq;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class TreeRenameTests
{
    [Fact]
    public void RenameItemCommand_StartsInlineEditingOnTheSelectedTreeNode()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        vm.SelectedTreeNode = movies;
        var requested = 0;
        vm.RenameRequested += (_, _) => requested++;

        Assert.True(vm.RenameItemCommand.CanExecute("tree"));
        vm.RenameItemCommand.Execute("tree");

        Assert.True(movies.IsRenaming);
        Assert.Equal("Movies", movies.RenameText);
        Assert.Null(movies.RenameError);
        Assert.Equal(1, requested);
        Assert.Same(movies, vm.SelectedTreeNode);
    }

    [Fact]
    public void CommitRename_PersistsTheNameAndUpdatesTheTreeLabel()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        vm.SelectedTreeNode = movies;

        vm.BeginRename(movies);
        movies.RenameText = "  Films  ";
        vm.CommitRename(movies);

        Assert.False(movies.IsRenaming);
        Assert.Equal(string.Empty, movies.RenameText);
        Assert.Equal("Films", movies.Title);
        Assert.Equal("Renamed to Films.", vm.StatusText);
        Assert.True(vm.IsDirtyDocument);

        vm.SelectedTreeNode = FindNode(vm, "library");
        Assert.Contains(vm.BrowserItems, item => item.Id == "movies" && item.Name == "Films");
    }

    [Fact]
    public void CommitRename_KeepsEditingAndExplainsAnEmptyName()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        vm.SelectedTreeNode = movies;

        vm.BeginRename(movies);
        movies.RenameText = "   ";
        vm.CommitRename(movies);

        Assert.True(movies.IsRenaming);
        Assert.True(movies.HasRenameError);
        Assert.Equal("Enter a name.", movies.RenameError);
        Assert.Equal("Enter a name.", vm.StatusText);
        Assert.Equal("Movies", movies.Title);
    }

    [Fact]
    public void EmptyNameError_IsClearedAfterAValidEdit()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        vm.SelectedTreeNode = movies;

        vm.BeginRename(movies);
        movies.RenameText = " ";
        vm.CommitRename(movies);
        Assert.True(movies.HasRenameError);

        movies.RenameText = "Films";
        vm.CommitRename(movies);

        Assert.False(movies.HasRenameError);
        Assert.Null(movies.RenameError);
        Assert.Equal("Films", movies.Title);
    }

    [Fact]
    public void CancelRename_DiscardsTheEditAndKeepsTheOriginalLabel()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        vm.SelectedTreeNode = movies;

        vm.BeginRename(movies);
        movies.RenameText = "Films";
        vm.CancelRename(movies);

        Assert.False(movies.IsRenaming);
        Assert.Equal("Movies", movies.Title);

        vm.SelectedTreeNode = FindNode(vm, "library");
        Assert.Contains(vm.BrowserItems, item => item.Id == "movies" && item.Name == "Movies");
    }

    [Fact]
    public void CancelRename_WithoutFocusRestore_DoesNotStealFocus()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        var focusRequests = 0;
        vm.FocusTreeRequested += (_, _) => focusRequests++;

        vm.BeginRename(movies);
        vm.CancelRename(movies, restoreFocus: false);

        Assert.Equal(0, focusRequests);
        Assert.False(movies.IsRenaming);
    }

    [Fact]
    public void CommitRename_RestoresFocusToTheTree()
    {
        var vm = CreateViewModel();
        var movies = FindNode(vm, "movies");
        var focusRequests = 0;
        vm.FocusTreeRequested += (_, _) => focusRequests++;

        vm.BeginRename(movies);
        movies.RenameText = "Films";
        vm.CommitRename(movies);

        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public void RenameItemCommand_RenamesTheLibraryRoot()
    {
        var vm = CreateViewModel();
        var library = vm.TreeNodes[0];
        vm.SelectedTreeNode = library;

        vm.RenameItemCommand.Execute("tree");
        library.RenameText = "My Library";
        vm.CommitRename(library);

        Assert.Equal("My Library", library.Title);
        Assert.Contains(vm.TreeContextMenuItems, item => item.Command == vm.RenameItemCommand);
    }

    [Fact]
    public void RenameItemCommand_RequiresASingleBrowserItemForTheListPane()
    {
        var vm = CreateViewModel();
        vm.SelectedTreeNode = FindNode(vm, "movies");
        vm.SelectedBrowserItems = new System.Collections.Generic.List<SkyCD.Documents.CatalogDocument>
        {
            vm.BrowserItems[0],
            vm.BrowserItems[1]
        };

        Assert.False(vm.RenameItemCommand.CanExecute("list"));
        Assert.True(vm.RenameItemCommand.CanExecute("tree"));
    }

    private static BrowserTreeNode FindNode(MainWindowViewModel vm, string key) =>
        vm.TreeNodes.SelectMany(Flatten).Single(node => node.Key == key);

    private static System.Collections.Generic.IEnumerable<BrowserTreeNode> Flatten(BrowserTreeNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
    }
}