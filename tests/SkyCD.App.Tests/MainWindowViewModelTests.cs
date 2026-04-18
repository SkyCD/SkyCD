using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_SelectsCatalogByDefault()
    {
        var vm = new MainWindowViewModel();

        Assert.Equal("Catalog", vm.CurrentPageTitle);
        Assert.Equal("catalog", vm.SelectedItem.Key);
    }

    [Fact]
    public void NavigateCommand_ChangesSelectedPage()
    {
        var vm = new MainWindowViewModel();

        vm.NavigateCommand.Execute("plugins");

        Assert.Equal("Plugins", vm.CurrentPageTitle);
        Assert.Equal("plugins", vm.SelectedItem.Key);
    }
}
