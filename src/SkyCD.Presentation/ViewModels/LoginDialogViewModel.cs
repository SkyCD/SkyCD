using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class LoginDialogViewModel : ObservableObject
{
    public bool CanConfirm =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool dialogAccepted;

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
