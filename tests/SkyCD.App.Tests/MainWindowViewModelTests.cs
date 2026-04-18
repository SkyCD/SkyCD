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
        Assert.False(vm.IsDeleteEnabled);
        Assert.Equal(BrowserViewMode.Details, vm.CurrentViewMode);
        Assert.Equal(BrowserSortMode.Name, vm.CurrentSortMode);
        Assert.Equal("library", vm.SelectedTreeNode?.Key);
        Assert.NotEmpty(vm.BrowserItems);
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
        Assert.Equal("Saved catalog.", vm.StatusText);
    }

    [Fact]
    public void DeleteCommand_EnabledOnlyWhenItemIsSelected()
    {
        var vm = new MainWindowViewModel();

        Assert.False(vm.DeleteItemCommand.CanExecute(null));

        vm.SelectedBrowserItem = vm.BrowserItems[0];

        Assert.True(vm.IsDeleteEnabled);
        Assert.True(vm.DeleteItemCommand.CanExecute(null));

        vm.DeleteItemCommand.Execute(null);

        Assert.Equal($"Deleted {vm.BrowserItems[0].Name}.", vm.StatusText);
    }
}
