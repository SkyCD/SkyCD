using SkyCD.Presentation.ViewModels;
using Xunit;

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

    [Fact]
    public void ConfirmCommand_AllowsAnonymousAccessWithoutAPassword()
    {
        var anonymousUser = new LoginDialogViewModel
        {
            Username = "anonymous"
        };
        Assert.True(anonymousUser.ConfirmCommand.CanExecute(null));

        var anonymousAccess = new LoginDialogViewModel
        {
            UseAnonymous = true
        };
        Assert.False(anonymousAccess.IsCredentialEntryEnabled);
        Assert.True(anonymousAccess.ConfirmCommand.CanExecute(null));

        anonymousAccess.ConfirmCommand.Execute(null);
        Assert.True(anonymousAccess.DialogAccepted);
    }
}