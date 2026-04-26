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
        Assert.False(vm.IsSortBySizeChecked);
    }

    [Fact]
    public void SetSortModeCommand_SizeSortMode_UpdatesCheckedState()
    {
        var vm = new MainWindowViewModel();

        vm.SetSortModeCommand.Execute("Size");

        Assert.Equal(BrowserSortMode.Size, vm.CurrentSortMode);
        Assert.True(vm.IsSortBySizeChecked);
        Assert.False(vm.IsSortByNameChecked);
        Assert.False(vm.IsSortByTypeChecked);
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

        vm.SetSortModeCommand.Execute("Size");
        var firstBySize = vm.BrowserItems[0].Name;

        Assert.NotEqual(firstByName, firstByType);
        Assert.Equal("Classical Collection", firstByName);
        Assert.Equal("Concert-2025.flac", firstByType);
        Assert.Equal("Concert-2025.flac", firstBySize);
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
    public void SetViewModeCommand_UpdatesPerModeVisibilityProperties()
    {
        var vm = new MainWindowViewModel();

        // Default: Details mode
        Assert.True(vm.IsDetailsMode);
        Assert.False(vm.IsListMode);
        Assert.False(vm.IsSmallIconsMode);
        Assert.False(vm.IsLargeIconsMode);
        Assert.False(vm.IsTilesMode);

        vm.SetViewModeCommand.Execute("List");
        Assert.False(vm.IsDetailsMode);
        Assert.True(vm.IsListMode);
        Assert.False(vm.IsSmallIconsMode);
        Assert.False(vm.IsLargeIconsMode);
        Assert.False(vm.IsTilesMode);

        vm.SetViewModeCommand.Execute("SmallIcons");
        Assert.False(vm.IsDetailsMode);
        Assert.False(vm.IsListMode);
        Assert.True(vm.IsSmallIconsMode);
        Assert.False(vm.IsLargeIconsMode);
        Assert.False(vm.IsTilesMode);

        vm.SetViewModeCommand.Execute("LargeIcons");
        Assert.False(vm.IsDetailsMode);
        Assert.False(vm.IsListMode);
        Assert.False(vm.IsSmallIconsMode);
        Assert.True(vm.IsLargeIconsMode);
        Assert.False(vm.IsTilesMode);

        vm.SetViewModeCommand.Execute("Tiles");
        Assert.False(vm.IsDetailsMode);
        Assert.False(vm.IsListMode);
        Assert.False(vm.IsSmallIconsMode);
        Assert.False(vm.IsLargeIconsMode);
        Assert.True(vm.IsTilesMode);
    }

    [Fact]
    public void SetViewModeCommand_UpdatesBrowserGridItemHeight()
    {
        var vm = new MainWindowViewModel();

        vm.SetViewModeCommand.Execute("LargeIcons");
        Assert.Equal(90, vm.BrowserGridItemHeight);

        vm.SetViewModeCommand.Execute("Tiles");
        Assert.Equal(80, vm.BrowserGridItemHeight);

        vm.SetViewModeCommand.Execute("SmallIcons");
        Assert.Equal(60, vm.BrowserGridItemHeight);
    }

    [Fact]
    public void AllViewModes_HaveExactlyOneCheckedState()
    {
        var vm = new MainWindowViewModel();
        var modes = new[] { "Details", "List", "SmallIcons", "LargeIcons", "Tiles" };

        foreach (var mode in modes)
        {
            vm.SetViewModeCommand.Execute(mode);
            var checkedCount = new[] { vm.IsDetailsViewChecked, vm.IsListViewChecked, vm.IsSmallIconsViewChecked, vm.IsLargeIconsViewChecked, vm.IsTilesViewChecked }
                .Count(c => c);
            Assert.Equal(1, checkedCount);
        }
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
    public void NavigateToFolderCommand_WithFolderSelection_SelectsMatchingTreeNode()
    {
        var vm = new MainWindowViewModel();
        var moviesFolder = vm.BrowserItems.Single(item => item.Name == "Movies");

        vm.SelectedBrowserItem = moviesFolder;
        vm.NavigateToFolderCommand.Execute(null);

        Assert.Equal("movies", vm.SelectedTreeNode?.Key);
        Assert.Equal("Navigated to Movies.", vm.StatusText);
    }

    [Fact]
    public void OpenCatalogCommand_DoesNotMarkDocumentDirty()
    {
        var vm = new MainWindowViewModel();

        vm.OpenCatalogCommand.Execute(null);

        Assert.False(vm.IsSaveEnabled);
        Assert.False(vm.SaveCatalogCommand.CanExecute(null));
        Assert.Equal("Done.", vm.StatusText);
    }

    [Fact]
    public void DeleteThenSave_UpdatesSaveCommandState()
    {
        var vm = new MainWindowViewModel();

        vm.DeleteItemCommand.Execute(null);

        Assert.True(vm.IsSaveEnabled);
        Assert.True(vm.SaveCatalogCommand.CanExecute(null));

        vm.CurrentCatalogPath = @"C:\tmp\catalog.scd";
        vm.SaveCatalogCommand.Execute(null);
        Assert.False(vm.IsSaveEnabled);
        Assert.False(vm.SaveCatalogCommand.CanExecute(null));
        Assert.Equal("Saved catalog to catalog.scd.", vm.StatusText);
    }

    [Fact]
    public void SaveCatalogCommand_WithSubscriber_OnlyRaisesRequest()
    {
        var vm = new MainWindowViewModel
        {
            IsDirtyDocument = true
        };
        var raised = false;
        vm.SaveCatalogRequested += (_, _) => raised = true;

        vm.SaveCatalogCommand.Execute(null);

        Assert.True(raised);
        Assert.True(vm.IsDirtyDocument);
    }

    [Fact]
    public void CompleteSaveCatalog_UsesFileNameForUnixStylePath()
    {
        var vm = new MainWindowViewModel();

        vm.CompleteSaveCatalog("/tmp/catalog.scd");

        Assert.Equal("Saved catalog to catalog.scd.", vm.StatusText);
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

        var deletedName = vm.BrowserItems[0].Name;
        vm.DeleteItemCommand.Execute(null);

        Assert.Equal($"Deleted {deletedName}.", vm.StatusText);
    }

    [Fact]
    public void DeleteCommand_RemovesItemFromVisibleList()
    {
        var vm = new MainWindowViewModel();
        var originalCount = vm.BrowserItems.Count;
        var deletedName = vm.SelectedBrowserItem?.Name;

        vm.DeleteItemCommand.Execute(null);

        Assert.Equal(originalCount - 1, vm.BrowserItems.Count);
        Assert.DoesNotContain(vm.BrowserItems, item => item.Name == deletedName);
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

    [Fact]
    public void AddItemCommand_RaisesAddToListRequest()
    {
        var vm = new MainWindowViewModel();
        var raised = false;
        vm.AddToListRequested += (_, _) => raised = true;

        vm.AddItemCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void AddImportedItem_AddsVisibleItemAndMarksDocumentDirty()
    {
        var vm = new MainWindowViewModel();
        var originalCount = vm.BrowserItems.Count;

        vm.AddImportedItem("Imported Folder");

        Assert.Equal(originalCount + 1, vm.BrowserItems.Count);
        Assert.Contains(vm.BrowserItems, item => item.Name == "Imported Folder");
        Assert.True(vm.IsDirtyDocument);
        Assert.Equal("Imported Folder", vm.SelectedBrowserItem?.Name);
    }

    [Fact]
    public void NewCatalogCommand_WithSubscriber_OnlyRaisesRequest()
    {
        var vm = new MainWindowViewModel();
        var raised = false;
        vm.NewCatalogRequested += (_, _) => raised = true;
        vm.IsDirtyDocument = true;

        vm.NewCatalogCommand.Execute(null);

        Assert.True(raised);
        Assert.True(vm.IsDirtyDocument);
    }

    [Fact]
    public void OpenCatalogCommand_WithSubscriber_OnlyRaisesRequest()
    {
        var vm = new MainWindowViewModel();
        var raised = false;
        vm.OpenCatalogRequested += (_, _) => raised = true;

        vm.OpenCatalogCommand.Execute(null);

        Assert.True(raised);
        Assert.False(vm.IsDirtyDocument);
    }

    [Fact]
    public void OpenAboutCommand_RaisesAboutRequest_WhenSubscriberIsPresent()
    {
        var vm = new MainWindowViewModel();
        var raised = false;
        vm.AboutRequested += (_, _) => raised = true;

        vm.OpenAboutCommand.Execute(null);

        Assert.True(raised);
        Assert.Equal("Done.", vm.StatusText);
    }

    [Fact]
    public void OpenAboutCommand_WithoutSubscriber_UsesFallbackStatus()
    {
        var vm = new MainWindowViewModel();

        vm.OpenAboutCommand.Execute(null);

        Assert.Equal("About dialog is not available.", vm.StatusText);
    }

    [Fact]
    public void OpenOptionsCommand_RaisesRequest_WhenSubscriberIsPresent()
    {
        var vm = new MainWindowViewModel();
        OptionsDialogRequestedEventArgs? request = null;
        vm.OptionsRequested += (_, args) => request = args;

        vm.OpenOptionsCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(2, request!.Dialog.Languages.Count);
        Assert.Equal("English", request.Dialog.Languages[0].Name);
        Assert.Equal("Lithuanian", request.Dialog.Languages[1].Name);

        request.Complete(true, @"C:\Plugins", "Lithuanian");
        Assert.Equal("Saved options (language: Lithuanian).", vm.StatusText);
    }

    [Fact]
    public void OpenOptionsCommand_WithoutSubscriber_UsesFallbackStatus()
    {
        var vm = new MainWindowViewModel();

        vm.OpenOptionsCommand.Execute(null);

        Assert.Equal("Options dialog is not available.", vm.StatusText);
    }

    [Fact]
    public void SaveCatalogAsCommand_WithSubscriber_OnlyRaisesRequest()
    {
        var vm = new MainWindowViewModel();
        var raised = false;
        vm.SaveCatalogAsRequested += (_, _) => raised = true;

        vm.SaveCatalogAsCommand.Execute(null);

        Assert.True(raised);
        Assert.Null(vm.CurrentCatalogPath);
    }

    [Fact]
    public void CompleteSaveCatalogAs_SetsCurrentPathAndClearsDirtyFlag()
    {
        var vm = new MainWindowViewModel
        {
            IsDirtyDocument = true
        };

        vm.CompleteSaveCatalogAs(@"C:\tmp\catalog.scd");

        Assert.False(vm.IsDirtyDocument);
        Assert.Equal(@"C:\tmp\catalog.scd", vm.CurrentCatalogPath);
        Assert.Equal("Saved catalog as catalog.scd.", vm.StatusText);
    }

    [Fact]
    public void CompleteSaveCatalogAs_UsesFileNameForUnixStylePath()
    {
        var vm = new MainWindowViewModel();

        vm.CompleteSaveCatalogAs("/tmp/catalog.scd");

        Assert.Equal("Saved catalog as catalog.scd.", vm.StatusText);
    }

    [Fact]
    public void OpenPropertiesCommand_RaisesRequestWithSelectedObjectValues()
    {
        var vm = new MainWindowViewModel();
        vm.SelectedTreeNode = vm.TreeNodes[0].Children.Single(node => node.Key == "movies");
        vm.SelectedBrowserItem = vm.BrowserItems.First(item => item.Type == "Video");
        PropertiesDialogRequestedEventArgs? request = null;
        vm.PropertiesRequested += (_, args) => request = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(vm.SelectedBrowserItem?.Name, request!.Dialog.Name);
        Assert.Equal(vm.SelectedBrowserItem?.IconGlyph, request.Dialog.IconGlyph);
        Assert.Equal(string.Empty, request.Dialog.Comments);
        Assert.NotEmpty(request.Dialog.InfoProperties);
    }

    [Fact]
    public void OpenPropertiesCommand_FolderItem_HidesInfoTab()
    {
        var vm = new MainWindowViewModel();
        vm.SelectedTreeNode = vm.TreeNodes[0];
        vm.SelectedBrowserItem = vm.BrowserItems.First(item => item.Type == "Folder");
        PropertiesDialogRequestedEventArgs? request = null;
        vm.PropertiesRequested += (_, args) => request = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        Assert.False(request!.Dialog.HasInfoTab);
        Assert.Empty(request.Dialog.InfoProperties);
    }

    [Fact]
    public void OpenPropertiesCommand_TreeNode_HidesInfoTab()
    {
        var vm = new MainWindowViewModel();
        vm.SelectedBrowserItem = null;
        vm.SelectedTreeNode = vm.TreeNodes[0];
        PropertiesDialogRequestedEventArgs? request = null;
        vm.PropertiesRequested += (_, args) => request = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        Assert.False(request!.Dialog.HasInfoTab);
        Assert.Empty(request.Dialog.InfoProperties);
    }

    [Fact]
    public void OpenPropertiesCommand_Accepted_PersistsCommentsAndMarksDocumentDirty()
    {
        var vm = new MainWindowViewModel();
        PropertiesDialogRequestedEventArgs? request = null;
        vm.PropertiesRequested += (_, args) => request = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        request!.Dialog.Comments = "Updated comment";
        request.Complete(true, request.Dialog.Comments);

        Assert.True(vm.IsDirtyDocument);
        Assert.Equal("Done.", vm.StatusText);

        vm.IsDirtyDocument = false;
        request = null;
        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal("Updated comment", request!.Dialog.Comments);
    }

    [Fact]
    public void OpenPropertiesCommand_Accepted_RenamesSelectedBrowserItem()
    {
        var vm = new MainWindowViewModel();
        PropertiesDialogRequestedEventArgs? request = null;
        vm.PropertiesRequested += (_, args) => request = args;

        var originalName = vm.SelectedBrowserItem!.Name;
        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        request!.Dialog.Name = "Renamed Item";
        request.Complete(true, request.Dialog.Comments);

        Assert.Equal("Renamed Item", vm.SelectedBrowserItem?.Name);
        Assert.DoesNotContain(vm.BrowserItems, item => item.Name == originalName);
    }

    [Fact]
    public void OpenPropertiesCommand_Canceled_DiscardsCommentChanges()
    {
        var vm = new MainWindowViewModel();
        PropertiesDialogRequestedEventArgs? request = null;
        vm.PropertiesRequested += (_, args) => request = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(request);
        request!.Dialog.Comments = "Draft comment";
        request.Complete(false, request.Dialog.Comments);

        Assert.False(vm.IsDirtyDocument);

        request = null;
        vm.OpenPropertiesCommand.Execute(null);
        Assert.NotNull(request);
        Assert.Equal(string.Empty, request!.Dialog.Comments);
    }

    [Fact]
    public void ApplySessionState_RestoresViewSortAndStatusBarAndRefreshesOrdering()
    {
        var vm = new MainWindowViewModel();
        var musicNode = vm.TreeNodes[0].Children.Single(node => node.Key == "music");
        vm.SelectedTreeNode = musicNode;

        vm.ApplySessionState(BrowserViewMode.LargeIcons, BrowserSortMode.Type, false);

        Assert.Equal(BrowserViewMode.LargeIcons, vm.CurrentViewMode);
        Assert.Equal(BrowserSortMode.Type, vm.CurrentSortMode);
        Assert.False(vm.IsStatusBarVisible);
        Assert.Equal("Concert-2025.flac", vm.BrowserItems[0].Name);
    }

    [Fact]
    public void AllSortModes_HaveExactlyOneCheckedState()
    {
        var vm = new MainWindowViewModel();

        foreach (BrowserSortMode mode in Enum.GetValues<BrowserSortMode>())
        {
            vm.SetSortModeCommand.Execute(mode.ToString());

            var checkedCount = new[] { vm.IsSortByNameChecked, vm.IsSortByTypeChecked, vm.IsSortBySizeChecked }
                .Count(c => c);
            Assert.Equal(1, checkedCount);
            Assert.Equal(mode, vm.CurrentSortMode);
        }
    }

    [Fact]
    public void Constructor_UsesInjectedDataStoreForTreeAndList()
    {
        var vm = new MainWindowViewModel(new StubBrowserDataStore());

        Assert.Equal("root", vm.SelectedTreeNode?.Key);
        Assert.Single(vm.BrowserItems);
        Assert.Equal("Sample.txt", vm.BrowserItems[0].Name);
    }

    private sealed class StubBrowserDataStore : IBrowserDataStore
    {
        public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
        {
            return [new BrowserTreeNode("root", "Root", "R", [], isExpanded: true)];
        }

        public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
        {
            return nodeKey == "root"
                ? [new BrowserItem("Sample.txt", "File", "12 KB", "F")]
                : [];
        }
    }
}
