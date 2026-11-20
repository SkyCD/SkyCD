using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class RemoteCredentialsForm : UserControl
{
    public RemoteCredentialsForm()
    {
        InitializeComponent();
    }

    private async void OnBrowseKeyFileClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LoginDialogViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.KeyFilePath = path;
        }
    }
}