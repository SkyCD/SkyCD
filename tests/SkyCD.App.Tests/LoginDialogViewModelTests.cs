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

    [Fact]
    public void FromAddress_PrefillsCredentialsEmbeddedInTheFtpUrl()
    {
        var viewModel = LoginDialogViewModel.FromAddress("ftp://alice:s3cret-pass@127.0.0.1/pub");

        Assert.Equal("alice", viewModel.Username);
        Assert.Equal("s3cret-pass", viewModel.Password);
        Assert.False(viewModel.UseAnonymous);
        Assert.True(viewModel.ConfirmCommand.CanExecute(null));

        var blank = LoginDialogViewModel.FromAddress("ftp://127.0.0.1/pub");
        Assert.Equal(string.Empty, blank.Username);
        Assert.Equal(string.Empty, blank.Password);
        Assert.False(blank.ConfirmCommand.CanExecute(null));
    }
}