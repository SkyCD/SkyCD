using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_InitializesLegacyShellDefaults()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.IsStatusBarVisible);
        Assert.Equal("Done.", vm.StatusText);
        Assert.False(vm.IsSaveEnabled);
        Assert.True(vm.IsDeleteEnabled);
        Assert.Equal(BrowserViewMode.Details, vm.CurrentViewMode);
        Assert.Equal(BrowserSortMode.Name, vm.CurrentSortMode);
        Assert.Equal("library", vm.SelectedTreeNode?.Key);
        Assert.NotEmpty(vm.BrowserItems);
        Assert.Equal(vm.BrowserItems[0], vm.SelectedBrowserItem);
    }

    [Fact]
    public void SetViewModeCommand_UpdatesModeAndCheckedState()
    {
        var vm = new MainWindowViewModel();

        vm.SetViewModeCommand.Execute("Tiles");

        Assert.Equal(BrowserViewMode.Tiles, vm.CurrentViewMode);
        Assert.True(vm.IsTilesViewChecked);
        Assert.False(vm.IsDetailsViewChecked);
    }

    [Fact]
    public void ToggleStatusBarCommand_ChangesVisibility()
    {
        var vm = new MainWindowViewModel();

        vm.ToggleStatusBarCommand.Execute(null);

        Assert.False(vm.IsStatusBarVisible);
    }

    [Fact]
    public void SetSortModeCommand_AppliesRequestedSortMode()
    {
        var vm = new MainWindowViewModel();

        vm.SetSortModeCommand.Execute("Type");

        Assert.Equal(BrowserSortMode.Type, vm.CurrentSortMode);
        Assert.True(vm.IsSortByTypeChecked);
        Assert.False(vm.IsSortByNameChecked);
    }

    [Fact]
    public void SetSortModeCommand_ChangesCurrentListOrdering()
    {
        var vm = new MainWindowViewModel();
        var musicNode = vm.TreeNodes[0].Children.Single(node => node.Key == "music");
        vm.SelectedTreeNode = musicNode;

        vm.SetSortModeCommand.Execute("Name");
        var firstByName = vm.BrowserItems[0].Name;

        vm.SetSortModeCommand.Execute("Type");
        var firstByType = vm.BrowserItems[0].Name;

        Assert.NotEqual(firstByName, firstByType);
        Assert.Equal("Classical Collection", firstByName);
        Assert.Equal("Concert-2025.flac", firstByType);
    }

    [Fact]
    public void SetViewModeCommand_UpdatesDerivedLayoutFlags()
    {
        var vm = new MainWindowViewModel();

        vm.SetViewModeCommand.Execute("LargeIcons");
        Assert.True(vm.IsIconGridMode);
        Assert.False(vm.IsListLikeMode);
        Assert.False(vm.IsTilesMode);
        Assert.Equal(24, vm.BrowserIconFontSize);

        vm.SetViewModeCommand.Execute("Tiles");
        Assert.True(vm.IsTilesMode);
        Assert.True(vm.IsIconGridMode);
        Assert.False(vm.IsListLikeMode);
        Assert.Equal(300, vm.BrowserGridItemWidth);

        vm.SetViewModeCommand.Execute("Details");
        Assert.True(vm.IsDetailsViewChecked);
        Assert.True(vm.IsListLikeMode);
        Assert.False(vm.IsIconGridMode);
        Assert.True(vm.ShowDetailsColumns);
    }

    [Fact]
    public void ExpandAndCollapseSelectionCommand_UpdatesSelectedTreeNodeExpansion()
    {
        var vm = new MainWindowViewModel();
        var moviesNode = vm.TreeNodes[0].Children.Single(node => node.Key == "movies");
        vm.SelectedTreeNode = moviesNode;

        vm.ExpandSelectionCommand.Execute("tree");
        Assert.True(moviesNode.IsExpanded);
        Assert.Equal("movies", vm.SelectedTreeNode?.Key);

        vm.CollapseSelectionCommand.Execute("tree");
        Assert.False(moviesNode.IsExpanded);
        Assert.Equal("movies", vm.SelectedTreeNode?.Key);
    }

    [Fact]
    public void ExpandSelectionCommand_FromListContext_TargetsMatchingFolderNode()
    {
        var vm = new MainWindowViewModel();
        var moviesFolder = vm.BrowserItems.Single(item => item.Name == "Movies");

        vm.SelectedBrowserItem = moviesFolder;
        vm.ExpandSelectionCommand.Execute("list");

        Assert.Equal("movies", vm.SelectedTreeNode?.Key);
        Assert.True(vm.SelectedTreeNode?.IsExpanded);
    }

    [Fact]
    public void OpenThenSave_UpdatesSaveCommandState()
    {
        var vm = new MainWindowViewModel();

        Assert.False(vm.SaveCatalogCommand.CanExecute(null));

        vm.OpenCatalogCommand.Execute(null);

        Assert.True(vm.IsSaveEnabled);
        Assert.True(vm.SaveCatalogCommand.CanExecute(null));

        vm.SaveCatalogCommand.Execute(null);

        Assert.False(vm.IsSaveEnabled);
        Assert.False(vm.SaveCatalogCommand.CanExecute(null));
        Assert.Equal("Done.", vm.StatusText);
    }

    [Fact]
    public void DeleteCommand_EnabledOnlyWhenItemIsSelected()
    {
        var vm = new MainWindowViewModel();
        vm.SelectedBrowserItem = null;

        Assert.False(vm.DeleteItemCommand.CanExecute(null));

        vm.SelectedBrowserItem = vm.BrowserItems[0];

        Assert.True(vm.IsDeleteEnabled);
        Assert.True(vm.DeleteItemCommand.CanExecute(null));

        vm.DeleteItemCommand.Execute(null);

        Assert.Equal($"Deleted {vm.BrowserItems[0].Name}.", vm.StatusText);
    }

    [Fact]
    public void SelectingDifferentTreeNode_RefreshesListAndSelectsFirstItem()
    {
        var vm = new MainWindowViewModel();
        var musicNode = vm.TreeNodes[0].Children.Single(node => node.Key == "music");

        vm.SelectedTreeNode = musicNode;

        Assert.NotEmpty(vm.BrowserItems);
        Assert.Equal("music", vm.SelectedTreeNode?.Key);
        Assert.Equal(vm.BrowserItems[0], vm.SelectedBrowserItem);
    }

    [Fact]
    public void TreeAndListItems_ExposeIconGlyphs()
    {
        var vm = new MainWindowViewModel();

        Assert.All(vm.TreeNodes, node => Assert.False(string.IsNullOrWhiteSpace(node.IconGlyph)));
        Assert.All(vm.TreeNodes.SelectMany(node => node.Children), node => Assert.False(string.IsNullOrWhiteSpace(node.IconGlyph)));
        Assert.All(vm.BrowserItems, item => Assert.False(string.IsNullOrWhiteSpace(item.IconGlyph)));
    }

    [Fact]
    public void OpenCatalogCommand_TracksLifecycleAndResetsProgressVisuals()
    {
        var vm = new MainWindowViewModel();

        vm.OpenCatalogCommand.Execute(null);

        Assert.False(vm.IsProgressVisible);
        Assert.Equal(0, vm.ProgressValue);
        Assert.Equal("Done.", vm.StatusText);
        Assert.Equal(["Loading catalog...", "Parsing catalog...", "Updating browser...", "Done."], vm.StatusTransitions);
        Assert.Equal([0, 35, 80, 100, 0], vm.ProgressTransitions);
    }

    [Fact]
    public void RefreshCommand_TracksUpdatingParsingLifecycle()
    {
        var vm = new MainWindowViewModel();

        vm.RefreshCommand.Execute(null);

        Assert.Equal(["Updating view...", "Parsing catalog...", "Done."], vm.StatusTransitions);
        Assert.Equal([0, 60, 100, 0], vm.ProgressTransitions);
        Assert.False(vm.IsProgressVisible);
    }
}
