using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

/// <summary>
/// UI Smoke tests for #142: Verify core UI functionality and parity with legacy shell.
/// These tests validate key dialog flows and command execution paths.
/// </summary>
public class UISmokeTests
{
    [Fact]
    public void MainWindow_ShellInitializesWithDefaultState()
    {
        var vm = new MainWindowViewModel();

        Assert.Equal(BrowserViewMode.Details, vm.CurrentViewMode);
        Assert.Equal(BrowserSortMode.Name, vm.CurrentSortMode);
        Assert.NotNull(vm.SelectedTreeNode);
        Assert.NotEmpty(vm.BrowserItems);
    }

    [Fact]
    public void BrowserViewMode_CanBeSwitched()
    {
        var vm = new MainWindowViewModel();
        var initialMode = vm.CurrentViewMode;

        vm.SetViewModeCommand.Execute("LargeIcons");
        var newMode = vm.CurrentViewMode;

        Assert.NotEqual(initialMode, newMode);
        Assert.Equal(BrowserViewMode.LargeIcons, newMode);
    }

    [Fact]
    public void BrowserSortMode_CanBeSwitched()
    {
        var vm = new MainWindowViewModel();
        var initialMode = vm.CurrentSortMode;

        vm.SetSortModeCommand.Execute("Type");
        var newMode = vm.CurrentSortMode;

        Assert.NotEqual(initialMode, newMode);
        Assert.Equal(BrowserSortMode.Type, newMode);
    }

    [Fact]
    public void OptionsDialog_InitializesLanguageSelection()
    {
        var languages = new[] { "English", "Lithuanian" };
        var vm = new OptionsDialogViewModel(languages);

        Assert.Equal(2, vm.Languages.Count);
        Assert.NotNull(vm.SelectedLanguage);
    }

    [Fact]
    public void OptionsDialog_RefreshPluginsCommand_Works()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var eventRaised = false;
        vm.RefreshPluginsRequested += (_, _) => eventRaised = true;

        vm.RefreshPluginsCommand.Execute(null);

        Assert.True(eventRaised);
    }

    [Fact]
    public void PropertiesDialog_ConfirmCommand_SetsDialogAccepted()
    {
        var vm = new PropertiesDialogViewModel(
            "item1",
            "Test Item",
            "📄",
            "Test comments",
            new Dictionary<string, object?>());

        Assert.False(vm.DialogAccepted);
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogAccepted);
    }

    [Fact]
    public void AddToListDialog_ValidatesInput()
    {
        var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.Folder,
            SourceValue = "",
            MediaName = ""
        };

        Assert.False(vm.CanConfirm);

        vm.SourceValue = "C:\\Music";
        vm.MediaName = "My Media";
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void LoginDialog_RequiresBothFields()
    {
        var vm = new LoginDialogViewModel();

        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Username = "testuser";
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Password = "password";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void AboutDialog_DisplaysApplicationInfo()
    {
        var vm = new AboutDialogViewModel("TestApp", "1.0.0", "https://example.com");

        Assert.Equal("TestApp", vm.ProductName);
        Assert.Equal("1.0.0", vm.Version);
        Assert.Equal("https://example.com", vm.Website);
    }

    [Fact]
    public void ProgressDialog_SupportsProgressTracking()
    {
        var vm = new AddingProgressDialogViewModel();

        Assert.Equal(0, vm.ProgressValue);
        vm.ProgressValue = 50;
        vm.OperationText = "Processing...";

        Assert.Equal(50, vm.ProgressValue);
        Assert.Equal("Processing...", vm.OperationText);
    }

    [Fact]
    public void MainShell_StatusBarToggle_Works()
    {
        var vm = new MainWindowViewModel();
        var initialState = vm.IsStatusBarVisible;

        vm.ToggleStatusBarCommand.Execute(null);

        Assert.NotEqual(initialState, vm.IsStatusBarVisible);
    }
}
