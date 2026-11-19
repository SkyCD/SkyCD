using System;
using SkyCD.Presentation.Scanning;
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

    [Fact]
    public void FromAddress_TlsModeDefaultsToTheSchemeAndEffectiveAddressKeepsTheChoice()
    {
        var implicitFtps = LoginDialogViewModel.FromAddress("ftps://127.0.0.1/pub");
        Assert.True(implicitFtps.IsFtp);
        Assert.Equal(RemoteTlsMode.Implicit, implicitFtps.TlsMode);
        Assert.StartsWith("ftps://127.0.0.1", implicitFtps.EffectiveAddress, StringComparison.Ordinal);
        Assert.DoesNotContain("tls=", implicitFtps.EffectiveAddress, StringComparison.Ordinal);

        var plain = LoginDialogViewModel.FromAddress("ftp://127.0.0.1:2121/pub");
        Assert.Equal(RemoteTlsMode.Off, plain.TlsMode);
        Assert.Equal("ftp://127.0.0.1:2121/pub", plain.EffectiveAddress);

        plain.TlsModeIndex = 1;
        Assert.Equal(RemoteTlsMode.Explicit, plain.TlsMode);
        Assert.Equal("ftp://127.0.0.1:2121/pub?tls=explicit", plain.EffectiveAddress);

        var queried = LoginDialogViewModel.FromAddress("ftp://127.0.0.1/pub?tls=explicit");
        Assert.Equal(RemoteTlsMode.Explicit, queried.TlsMode);
        Assert.Contains("tls=explicit", queried.EffectiveAddress, StringComparison.Ordinal);

        implicitFtps.TlsModeIndex = 0;
        Assert.Equal("ftps://127.0.0.1:990/pub?tls=off", implicitFtps.EffectiveAddress);
    }
}