using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.SDK.Capabilities.Accounts;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    public const string AccountsCategoryId = "accounts";
    private const int AccountsCategoryIndex = 13;

    private CancellationTokenSource? accountsCancellation;

    private HashSet<string>? catalogAccountFilter;

    /// <summary>Account connectors for the open catalog (filtered from the enabled plugins).</summary>
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

    /// <summary>
    /// Scopes the Options accounts list to the open catalog. Only connectors with an account folder
    /// in the given documents stay visible; a catalog without folders shows an empty list. Null
    /// clears the filter (all discovered connectors).
    /// </summary>
    public void SetCatalogAccountFolders(IEnumerable<CatalogDocument>? catalogDocuments)
    {
        if (catalogDocuments is null)
        {
            catalogAccountFilter = null;
        }
        else
        {
            var folders = SkyCD.Presentation.Accounts.CatalogAccountReader.ReadAccountFolders(catalogDocuments);
            catalogAccountFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                catalogAccountFilter.Add(folder.ProviderId + "\n" + folder.AccountKey);
            }
        }

        ApplyCatalogFilterAfterRefresh();
        OnPropertyChanged(nameof(HasAccounts));
    }

    public async Task RefreshAccountsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var account in Accounts.ToList())
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

        ApplyCatalogFilterAfterRefresh();
        OnPropertyChanged(nameof(HasAccounts));
    }

    private void ApplyCatalogFilterAfterRefresh()
    {
        if (catalogAccountFilter is null)
        {
            return;
        }

        for (var index = Accounts.Count - 1; index >= 0; index--)
        {
            var status = Accounts[index].LastStatus;
            if (status is null)
            {
                continue;
            }

            var key = status.ProviderId + "\n" + status.AccountId;
            if (!catalogAccountFilter.Contains(key))
            {
                Accounts.RemoveAt(index);
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
