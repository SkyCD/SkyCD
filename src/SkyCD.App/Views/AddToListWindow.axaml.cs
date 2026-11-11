using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class AddToListWindow : Window
{
    public AddToListWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not AddToListWindow window)
        {
            return;
        }

        if (window.DataContext is AddToListDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AddToListDialogViewModel vm &&
            e.PropertyName == nameof(AddToListDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }

    private async void OnBrowseFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddToListDialogViewModel vm)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current.Get(UiKeys.SelectFolder),
            AllowMultiple = false
        });

        var localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            vm.SourceValue = localPath;
            if (string.IsNullOrWhiteSpace(vm.MediaName))
            {
                var trimmed = localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var folderName = Path.GetFileName(trimmed);
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    folderName = trimmed;
                }
                vm.MediaName = folderName;
            }
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}