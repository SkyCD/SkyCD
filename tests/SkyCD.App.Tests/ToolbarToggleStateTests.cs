using System.Linq;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1004: the toolbar must expose the same toggle state as the View menu for the Status Bar,
/// Preview Pane and the five view modes.
/// </summary>
public class ToolbarToggleStateTests
{
    [Fact]
    public void ToolbarCommands_MarkViewTogglesAndReportCurrentState()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var commands = vm.ToolbarCommands();

        var statusBar = Assert.Single(commands, command => command.Id == ToolbarCommandIds.StatusBar);
        var preview = Assert.Single(commands, command => command.Id == ToolbarCommandIds.PreviewPane);
        var details = Assert.Single(commands, command => command.Id == ToolbarCommandIds.ViewDetails);
        var tiles = Assert.Single(commands, command => command.Id == ToolbarCommandIds.ViewTiles);
        var find = Assert.Single(commands, command => command.Id == ToolbarCommandIds.Find);
        var newCatalog = Assert.Single(commands, command => command.Id == ToolbarCommandIds.NewCatalog);

        Assert.True(statusBar.IsToggle);
        Assert.True(statusBar.IsChecked);
        Assert.True(preview.IsToggle);
        Assert.True(preview.IsChecked);
        Assert.True(details.IsToggle);
        Assert.True(details.IsChecked);
        Assert.True(tiles.IsToggle);
        Assert.False(tiles.IsChecked);
        Assert.True(find.IsToggle);
        Assert.False(find.IsChecked);
        Assert.False(newCatalog.IsToggle);
        Assert.False(newCatalog.IsChecked);
    }

    [Fact]
    public void FindToggle_FollowsQuickFilterOpenStateAndNotifiesTheToolbar()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = 0;
        vm.ToolbarStateChanged += (_, _) => raised++;

        var find = vm.ToolbarCommands().Single(command => command.Id == ToolbarCommandIds.Find);
        Assert.False(find.IsChecked);

        vm.FindCatalogCommand.Execute(null);

        Assert.True(vm.IsQuickFilterOpen);
        Assert.True(vm.ToolbarCommands().Single(command => command.Id == ToolbarCommandIds.Find).IsChecked);
        Assert.Equal(1, raised);

        vm.CloseQuickFilterCommand.Execute(null);

        Assert.False(vm.ToolbarCommands().Single(command => command.Id == ToolbarCommandIds.Find).IsChecked);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void ToolbarCommands_ReflectToggledStateAfterCommandsRun()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        vm.ToggleStatusBarCommand.Execute(null);
        vm.TogglePreviewPaneCommand.Execute(null);
        vm.SetViewModeCommand.Execute("Tiles");

        var commands = vm.ToolbarCommands();

        Assert.False(commands.Single(command => command.Id == ToolbarCommandIds.StatusBar).IsChecked);
        Assert.False(commands.Single(command => command.Id == ToolbarCommandIds.PreviewPane).IsChecked);
        Assert.True(commands.Single(command => command.Id == ToolbarCommandIds.ViewTiles).IsChecked);
        Assert.False(commands.Single(command => command.Id == ToolbarCommandIds.ViewDetails).IsChecked);
        Assert.Single(
            commands,
            command => command.Id.StartsWith("view-", System.StringComparison.Ordinal) && command.IsChecked);
    }

    [Fact]
    public void ToolbarStateChanged_RaisedWhenTogglesChangeElsewhere()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = 0;
        vm.ToolbarStateChanged += (_, _) => raised++;

        vm.ToggleStatusBarCommand.Execute(null);
        Assert.Equal(1, raised);

        vm.TogglePreviewPaneCommand.Execute(null);
        Assert.Equal(2, raised);

        vm.SetViewModeCommand.Execute("List");
        Assert.Equal(3, raised);

        // Re-selecting the active mode does not change state, so the toolbar is not touched.
        vm.SetViewModeCommand.Execute("List");
        Assert.Equal(3, raised);
    }
}
