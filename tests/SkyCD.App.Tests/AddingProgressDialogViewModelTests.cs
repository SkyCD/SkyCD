using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class AddingProgressDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesOperationText()
    {
        var vm = new AddingProgressDialogViewModel();

        Assert.Equal("Preparing database for modifications...", vm.OperationText);
    }

    [Fact]
    public void Constructor_InitializesProgressValueToZero()
    {
        var vm = new AddingProgressDialogViewModel();

        Assert.Equal(0, vm.ProgressValue);
    }

    [Fact]
    public void OperationText_CanBeUpdated()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.OperationText = "Scanning files...";

        Assert.Equal("Scanning files...", vm.OperationText);
    }

    [Fact]
    public void ProgressValue_CanBeUpdated()
    {
        var vm = new AddingProgressDialogViewModel();

        vm.ProgressValue = 50;

        Assert.Equal(50, vm.ProgressValue);
    }

    [Fact]
    public void ProgressValue_SupportsIncrement()
    {
        var vm = new AddingProgressDialogViewModel();

        for (int i = 1; i <= 100; i++)
        {
            vm.ProgressValue = i;
        }

        Assert.Equal(100, vm.ProgressValue);
    }
}
