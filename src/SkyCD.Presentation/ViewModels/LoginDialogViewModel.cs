using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Scanning;

namespace SkyCD.Presentation.ViewModels;

public partial class LoginDialogViewModel : ObservableObject
{
    public bool CanConfirm =>
        UseAnonymous ||
        (!string.IsNullOrWhiteSpace(Username) &&
         (!string.IsNullOrWhiteSpace(Password) || FtpCredentials.IsAnonymousUserName(Username)));

    public bool IsCredentialEntryEnabled => !UseAnonymous;

    [ObservableProperty] private string username = string.Empty;

    [ObservableProperty] private string password = string.Empty;

    [ObservableProperty] private bool useAnonymous;

    [ObservableProperty] private bool dialogAccepted;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        DialogAccepted = true;
    }

    partial void OnUsernameChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnPasswordChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnUseAnonymousChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCredentialEntryEnabled));
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}