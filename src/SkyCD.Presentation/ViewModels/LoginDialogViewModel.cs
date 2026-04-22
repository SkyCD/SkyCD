using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class LoginDialogViewModel : ObservableObject
{
    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty] private string password = string.Empty;

    [ObservableProperty] private string username = string.Empty;

    public bool CanConfirm =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);

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
}