using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

/// <summary>
/// The split form. The view owns the window lifetime and the folder picker; the view model owns the
/// split options, validation and progress.
/// </summary>
public partial class SplitWindow : Window
{
    private SplitDialogViewModel? boundViewModel;

    public SplitWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ReferenceEquals(boundViewModel, DataContext))
        {
            return;
        }

        if (boundViewModel is not null)
        {
            boundViewModel.CloseRequested -= CloseWindow;
        }

        boundViewModel = DataContext as SplitDialogViewModel;
        if (boundViewModel is not null)
        {
            boundViewModel.CloseRequested += CloseWindow;
        }
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SplitDialogViewModel viewModel)
        {
            return;
        }

        var picker = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (picker is null)
        {
            return;
        }

        var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current.Get(UiKeys.SplitOutputFolder),
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.OutputFolder = path;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => CloseWindow();

    private void CloseWindow()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }
}
