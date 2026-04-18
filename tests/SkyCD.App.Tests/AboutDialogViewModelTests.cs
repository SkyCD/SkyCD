using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class AboutDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var vm = new AboutDialogViewModel();

        Assert.Equal("SkyCD", vm.ProductName);
        Assert.Equal("3.0.0", vm.Version);
        Assert.Equal("https://github.com/SkyCD/SkyCD", vm.Website);
    }

    [Fact]
    public void Constructor_InitializesWithCustomValues()
    {
        var vm = new AboutDialogViewModel("MyApp", "1.2.3", "https://example.com");

        Assert.Equal("MyApp", vm.ProductName);
        Assert.Equal("1.2.3", vm.Version);
        Assert.Equal("https://example.com", vm.Website);
    }

    [Fact]
    public void DialogAccepted_DefaultsToFalse()
    {
        var vm = new AboutDialogViewModel();

        Assert.False(vm.DialogAccepted);
    }

    [Fact]
    public void ConfirmCommand_SetsDialogAcceptedTrue()
    {
        var vm = new AboutDialogViewModel();

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
    }
}
