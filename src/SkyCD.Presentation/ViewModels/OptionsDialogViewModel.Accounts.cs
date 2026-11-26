using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Accounts;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    public const string AccountsCategoryId = "accounts";
    private const int AccountsCategoryIndex = 13;

    private CancellationTokenSource? accountsCancellation;

    /// <summary>Account connectors discovered from the enabled plugins.</summary>
    public ObservableCollection<AccountConnectorItemViewModel> Accounts { get; } = [];

    public bool IsAccountsCategorySelected => SelectedBuiltInIndex == AccountsCategoryIndex;

    public bool ShowAccountsSection => IsAccountsCategorySelected && IsCurrentCategoryVisibleInSearch;

    public bool HasAccounts => Accounts.Count > 0;

    public void SetAccountSessions(IEnumerable<IAccountSession>? sessions)
    {
        DisposeAccounts();
        foreach (var session in sessions ?? [])
        {
            Accounts.Add(new AccountConnectorItemViewModel(session));
        }

        accountsCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(HasAccounts));
        NotifySettingsVisibilityChanged();
        _ = RefreshAccountsAsync(accountsCancellation.Token);
    }

    public async Task RefreshAccountsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var account in Accounts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await account.RefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void RelocalizeAccounts()
    {
        foreach (var account in Accounts)
        {
            account.Relocalize();
        }
    }

    private void DisposeAccounts()
    {
        accountsCancellation?.Cancel();
        accountsCancellation?.Dispose();
        accountsCancellation = null;
        Accounts.Clear();
        OnPropertyChanged(nameof(HasAccounts));
    }
}
