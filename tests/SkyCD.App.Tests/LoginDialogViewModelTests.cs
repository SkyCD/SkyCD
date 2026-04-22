using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Tests;

public class LoginDialogViewModelTests
{
    [Fact]
    public void ConfirmCommand_RequiresBothUsernameAndPassword()
    {
        var vm = new LoginDialogViewModel();

        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Username = "user";
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Password = "secret";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmCommand_SetsDialogAccepted_WhenCredentialsArePresent()
    {
        var vm = new LoginDialogViewModel
        {
            Username = "user",
            Password = "secret"
        };

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
    }
}