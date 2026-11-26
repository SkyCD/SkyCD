using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OptionsDialogViewModelAccountsTests
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

    private static AccountSessionStatus CreateStatus(bool connected = true) => new()
    {
        ProviderId = "jetbrains",
        DisplayName = "JetBrains",
        AccountId = "default",
        AccountEmail = "dev@example.com",
        IsConnected = connected,
        Products =
        [
            new AccountOwnedProduct { Id = "L1", Product = "IntelliJ IDEA Ultimate", Status = "active" }
        ]
    };

    [Fact]
    public void AccountsCategory_IsAlwaysListed()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        var category = Assert.Single(viewModel.SettingCategories,
            item => item.Id == OptionsDialogViewModel.AccountsCategoryId);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Accounts), category.Title);
        Assert.Contains(viewModel.SidebarEntries,
            entry => entry.Category?.Id == OptionsDialogViewModel.AccountsCategoryId);
    }

    [Fact]
    public void SelectingAccountsCategory_ShowsSectionEvenWithoutConnectors()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.Id == OptionsDialogViewModel.AccountsCategoryId);

        Assert.True(viewModel.IsAccountsCategorySelected);
        Assert.True(viewModel.ShowAccountsSection);
        Assert.True(viewModel.HasVisibleCategoryContent);
        Assert.False(viewModel.HasAccounts);
    }

    [Fact]
    public async Task SetAccountSessions_ListsConnectorsAndReadsTheirStatus()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetAccountSessions([new FakeAccountSession(CreateStatus())]);

        await viewModel.RefreshAccountsAsync();

        var account = Assert.Single(viewModel.Accounts);
        Assert.True(viewModel.HasAccounts);
        Assert.Equal("JetBrains", account.ProviderName);
        Assert.True(account.IsConnected);
        Assert.Contains("dev@example.com", account.StateText);
        Assert.Contains("1", account.ProductCountText);
        Assert.False(account.HasError);
    }

    [Fact]
    public async Task DisconnectedConnector_OffersConnectOnly()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetAccountSessions([new FakeAccountSession(CreateStatus(connected: false))]);

        await viewModel.RefreshAccountsAsync();

        var account = Assert.Single(viewModel.Accounts);
        Assert.False(account.IsConnected);
        Assert.True(account.ShowConnect);
        Assert.False(account.ShowManage);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.AccountNotSignedIn), account.StateText);
    }

    [Fact]
    public void RestoreSettingCategory_ResolvesAccountsById()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        viewModel.RestoreSettingCategory(OptionsDialogViewModel.AccountsCategoryId, legacyTabIndex: 0);

        Assert.Equal(OptionsDialogViewModel.AccountsCategoryId, viewModel.SelectedSettingCategory?.Id);
        Assert.Equal(OptionsDialogViewModel.AccountsCategoryId, viewModel.SelectedSidebarEntry?.Category?.Id);
    }
}
