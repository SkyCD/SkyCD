using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class AddToListAccountSourceTests
{
    private sealed class FakeAccountSession(AccountSessionStatus status) : IAccountSession
    {
        public bool SignedOut { get; private set; }

        public bool Synced { get; private set; }

        public Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountConnectionResult { Success = true, AccountId = status.AccountId });

        public Task<AccountSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status with { IsConnected = status.IsConnected && !SignedOut });

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task SyncAsync(CancellationToken cancellationToken = default)
        {
            Synced = true;
            return Task.CompletedTask;
        }
    }

    private static AccountSessionStatus ConnectedStatus() => new()
    {
        ProviderId = "jetbrains",
        DisplayName = "JetBrains",
        AccountId = "default",
        AccountEmail = "dev@example.com",
        IsConnected = true,
        Products =
        [
            new AccountOwnedProduct
            {
                Id = "L1",
                Product = "IntelliJ IDEA Ultimate",
                Status = "active"
            },
            new AccountOwnedProduct
            {
                Id = "L2",
                Product = "PhpStorm",
                Status = "active"
            }
        ]
    };

    [Fact]
    public async Task ConnectedAccounts_BecomeSelectableSourceChoices()
    {
        var session = new FakeAccountSession(ConnectedStatus());
        using var vm = new AddToListDialogViewModel(accountSessions: [session])
        {
            SourceMode = AddToListSourceMode.FromAccount
        };

        await vm.RefreshAccountsAsync();

        var choice = Assert.Single(vm.AccountChoices);
        Assert.Same(choice, vm.SelectedAccount);
        Assert.Contains("JetBrains", choice.Name, StringComparison.Ordinal);
        Assert.Equal(2, choice.Products.Count);
        Assert.True(vm.ShowAccountPicker);
        Assert.False(vm.ShowAccountEmptyState);
        Assert.True(vm.CanConfirm);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task NoConnectedAccounts_ShowsEmptyStateAndBlocksConfirm()
    {
        var session = new FakeAccountSession(ConnectedStatus() with { IsConnected = false });
        using var vm = new AddToListDialogViewModel(accountSessions: [session])
        {
            SourceMode = AddToListSourceMode.FromAccount
        };

        await vm.RefreshAccountsAsync();

        Assert.Empty(vm.AccountChoices);
        Assert.True(vm.ShowAccountEmptyState);
        Assert.False(vm.ShowAccountPicker);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task DisconnectedConnector_OffersConnectOnly()
    {
        var session = new FakeAccountSession(ConnectedStatus() with { IsConnected = false });
        using var vm = new AddToListDialogViewModel(accountSessions: [session])
        {
            SourceMode = AddToListSourceMode.FromAccount
        };

        await vm.RefreshAccountsAsync();

        var connector = Assert.Single(vm.AvailableAccountConnectors);
        Assert.True(connector.ShowConnect);
        Assert.False(connector.ShowManage);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.AccountNotSignedIn), connector.StateText);
    }

    [Fact]
    public async Task ConnectedConnector_OffersSyncAndSignOut()
    {
        var session = new FakeAccountSession(ConnectedStatus());
        using var vm = new AddToListDialogViewModel(accountSessions: [session])
        {
            SourceMode = AddToListSourceMode.FromAccount
        };

        await vm.RefreshAccountsAsync();

        var connector = Assert.Single(vm.AvailableAccountConnectors);
        Assert.False(connector.ShowConnect);
        Assert.True(connector.ShowManage);
    }

    [Fact]
    public async Task SyncConnector_RunsWithoutLeavingTheDialog()
    {
        var session = new FakeAccountSession(ConnectedStatus());
        using var vm = new AddToListDialogViewModel(accountSessions: [session])
        {
            SourceMode = AddToListSourceMode.FromAccount
        };
        await vm.RefreshAccountsAsync();

        await vm.AvailableAccountConnectors[0].SyncCommand.ExecuteAsync(null);

        Assert.True(session.Synced);
    }

    [Fact]
    public async Task SignOutConnector_ReturnsItToTheConnectStateAndDropsTheChoice()
    {
        var session = new FakeAccountSession(ConnectedStatus());
        using var vm = new AddToListDialogViewModel(accountSessions: [session])
        {
            SourceMode = AddToListSourceMode.FromAccount
        };
        await vm.RefreshAccountsAsync();
        Assert.Single(vm.AccountChoices);

        await vm.AvailableAccountConnectors[0].SignOutCommand.ExecuteAsync(null);

        Assert.True(session.SignedOut);
        Assert.True(vm.AvailableAccountConnectors[0].ShowConnect);
        Assert.False(vm.AvailableAccountConnectors[0].ShowManage);
        Assert.Empty(vm.AccountChoices);
        Assert.True(vm.ShowAccountEmptyState);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void FromAccount_HidesNameAndDestinationSections()
    {
        using var vm = new AddToListDialogViewModel
        {
            SourceMode = AddToListSourceMode.FromAccount
        };

        Assert.False(vm.IsNameEntryVisible);
        Assert.False(vm.IsDestinationSectionVisible);
        Assert.True(vm.IsSourceFromAccount);
        Assert.Equal(AddToListTargetPlacement.SelectedFolder, vm.TargetPlacement);
    }

    [Fact]
    public void SelectingFromAccount_ForcesTheSelectedFolderDestination()
    {
        using var vm = new AddToListDialogViewModel
        {
            TargetPlacement = AddToListTargetPlacement.NewMedia
        };

        vm.SelectSourceCommand.Execute("FromAccount");

        Assert.Equal(AddToListSourceMode.FromAccount, vm.SourceMode);
        Assert.Equal(AddToListTargetPlacement.SelectedFolder, vm.TargetPlacement);
    }

    [Fact]
    public async Task RefreshAccounts_ReplacesChoicesWithoutDuplicates()
    {
        var session = new FakeAccountSession(ConnectedStatus());
        using var vm = new AddToListDialogViewModel(accountSessions: [session]);

        await vm.RefreshAccountsAsync();
        await vm.RefreshAccountsAsync();

        Assert.Single(vm.AccountChoices);
        Assert.True(vm.SelectedAccount is not null);
    }
}
