using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Accounts;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class PerCatalogAddToListTests
{
    private sealed class FakeAccountSession(AccountSessionStatus status) : IAccountSession
    {
        public Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountConnectionResult { Success = true, AccountId = status.AccountId });

        public Task<AccountSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static AccountSessionStatus ConnectedStatus(bool connected = true) => new()
    {
        ProviderId = "jetbrains",
        DisplayName = "JetBrains",
        AccountId = "default",
        AccountEmail = "dev@example.com",
        IsConnected = connected,
        Products = connected
            ?
            [
                new AccountOwnedProduct { Id = "L1", Product = "IntelliJ IDEA Ultimate", Status = "active" },
            ]
            : [],
    };

    private static CatalogDocument AccountFolderDocument() => new()
    {
        Id = "account-folder-jetbrains-default",
        Name = "JetBrains — dev@example.com",
        ParentId = "parent-1",
        Type = CatalogDocumentType.Folder,
        Size = 0,
        ChildrenCount = 0,
        Properties = new PropertiesCollection
        {
            [CatalogDocument.SizeKnownPropertyKey] = false,
            [CatalogAccountReader.ProviderKey] = "jetbrains",
            [CatalogAccountReader.KeyKey] = "default",
            [CatalogAccountReader.EmailKey] = "dev@example.com",
            [CatalogAccountReader.DisplayNameKey] = "JetBrains",
            [CatalogAccountReader.ConnectedKey] = true,
        },
    };

    [Fact]
    public async Task CatalogWithoutFolders_ShowsInlineAddFormEvenWhenSessionConnected()
    {
        using var vm = new AddToListDialogViewModel(accountSessions: [new FakeAccountSession(ConnectedStatus())])
        {
            SourceMode = AddToListSourceMode.FromAccount,
        };
        vm.SetCatalogAccountFolders([]);

        await vm.RefreshAccountsAsync();

        Assert.Empty(vm.AccountChoices);
        Assert.True(vm.ShowAccountEmptyState);
        Assert.False(vm.ShowAccountPicker);
        Assert.NotEmpty(vm.AvailableAccountConnectors);
    }

    [Fact]
    public async Task CatalogWithFolder_ShowsConnectedChoice()
    {
        using var vm = new AddToListDialogViewModel(accountSessions: [new FakeAccountSession(ConnectedStatus())])
        {
            SourceMode = AddToListSourceMode.FromAccount,
        };
        vm.SetCatalogAccountFolders([AccountFolderDocument()]);

        await vm.RefreshAccountsAsync();

        var choice = Assert.Single(vm.AccountChoices);
        Assert.True(choice.Status.IsConnected);
        Assert.True(vm.ShowAccountPicker);
        Assert.False(vm.ShowAccountEmptyState);
        Assert.False(vm.SelectedAccountNeedsSignIn);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public async Task CatalogWithFolderButDisconnectedSession_ShowsSignInRequired()
    {
        using var vm = new AddToListDialogViewModel(accountSessions: [new FakeAccountSession(ConnectedStatus(connected: false))])
        {
            SourceMode = AddToListSourceMode.FromAccount,
        };
        vm.SetCatalogAccountFolders([AccountFolderDocument()]);

        await vm.RefreshAccountsAsync();

        var choice = Assert.Single(vm.AccountChoices);
        Assert.False(choice.Status.IsConnected);
        Assert.True(vm.ShowAccountPicker);
        Assert.True(vm.SelectedAccountNeedsSignIn);
        Assert.False(vm.CanConfirm);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.AccountNotSignedIn), vm.ValidationMessage);
    }
}
