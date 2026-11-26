using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>One discovered account connector on the Options Accounts page.</summary>
public partial class AccountConnectorItemViewModel : ObservableObject
{
    private readonly IAccountSession session;
    private AccountSessionStatus? statusSnapshot;

    public AccountConnectorItemViewModel(IAccountSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        providerName = string.Empty;
        stateText = string.Empty;
    }

    [ObservableProperty] private string providerName;

    [ObservableProperty] private bool isConnected;

    [ObservableProperty] private string stateText;

    [ObservableProperty] private string lastSyncedText = string.Empty;

    [ObservableProperty] private string productCountText = string.Empty;

    [ObservableProperty] private string? errorMessage;

    [ObservableProperty] private bool isBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasProductCount => !string.IsNullOrWhiteSpace(ProductCountText);

    /// <summary>Connect only makes sense before the account is signed in.</summary>
    public bool CanConnect => !IsConnected && !IsBusy;

    /// <summary>Sync and sign-out need a signed-in account.</summary>
    public bool CanManage => IsConnected && !IsBusy;

    public bool ShowConnect => !IsConnected;

    public bool ShowManage => IsConnected;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ApplyStatus(await session.GetStatusAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (string.IsNullOrEmpty(ProviderName))
            {
                ProviderName = LocalizationService.Current.Get(UiKeys.AccountUnknownProvider);
                StateText = LocalizationService.Current.Get(UiKeys.AccountNotSignedIn);
            }

            ErrorMessage = LocalizationService.Current.Format(UiKeys.AccountStatusError, exception.Message);
        }
    }

    /// <summary>Re-applies the last known state after a language change.</summary>
    public void Relocalize()
    {
        if (statusSnapshot is { } status)
        {
            ApplyStatus(status);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => RunAsync(async cancellationToken =>
    {
        var result = await session.ConnectAsync(cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.Error ?? LocalizationService.Current.Get(UiKeys.AccountSignInFailed);
            return;
        }

        ApplyStatus(await session.GetStatusAsync(cancellationToken));
    }, UiKeys.AccountSignInFailed);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task SyncAsync() => RunAsync(async cancellationToken =>
    {
        await session.SyncAsync(cancellationToken);
        ApplyStatus(await session.GetStatusAsync(cancellationToken));
    }, UiKeys.AccountSyncFailed);

    [RelayCommand(CanExecute = nameof(CanManage))]
    private Task SignOutAsync() => RunAsync(async cancellationToken =>
    {
        await session.SignOutAsync(cancellationToken);
        ApplyStatus(await session.GetStatusAsync(cancellationToken));
    }, UiKeys.AccountSignOutFailed);

    private async Task RunAsync(Func<CancellationToken, Task> action, string failureKey)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await action(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = LocalizationService.Current.Format(failureKey, exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyStatus(AccountSessionStatus status)
    {
        statusSnapshot = status;
        ProviderName = string.IsNullOrWhiteSpace(status.DisplayName)
            ? LocalizationService.Current.Get(UiKeys.AccountUnknownProvider)
            : status.DisplayName;
        var accountName = string.IsNullOrWhiteSpace(status.AccountEmail) ? status.AccountId : status.AccountEmail;
        IsConnected = status.IsConnected;
        StateText = status.IsConnected
            ? LocalizationService.Current.Format(UiKeys.AccountSignedInAs, accountName)
            : LocalizationService.Current.Get(UiKeys.AccountNotSignedIn);
        LastSyncedText = status.LastSyncedUtc is { } synced
            ? LocalizationService.Current.Format(UiKeys.AccountLastSynced, synced.ToLocalTime().ToString("g"))
            : LocalizationService.Current.Get(UiKeys.AccountNeverSynced);
        ProductCountText = status.IsConnected
            ? LocalizationService.Current.Format(UiKeys.AccountProductCount, status.Products.Count)
            : string.Empty;
        OnPropertyChanged(nameof(HasProductCount));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsBusyChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
        SignOutCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowConnect));
        OnPropertyChanged(nameof(ShowManage));
        ConnectCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
        SignOutCommand.NotifyCanExecuteChanged();
    }
}
