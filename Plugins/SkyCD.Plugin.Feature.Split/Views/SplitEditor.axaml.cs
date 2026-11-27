using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Feature.Split.Localization;
using SkyCD.Plugin.Feature.Split.ViewModels;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Split.Views;

/// <summary>
/// The split form, embedded in a host-owned window. The view runs the folder picker and closes that host
/// window; the view model owns the split options, validation and progress.
/// </summary>
public partial class SplitEditor : UserControl
{
    private SplitDialogViewModel? boundViewModel;

    public SplitEditor() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ReferenceEquals(boundViewModel, DataContext))
        {
            return;
        }

        if (boundViewModel is not null)
        {
            boundViewModel.CloseRequested -= CloseHostWindow;
        }

        boundViewModel = DataContext as SplitDialogViewModel;
        if (boundViewModel is not null)
        {
            boundViewModel.CloseRequested += CloseHostWindow;
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
            Title = LocalizationService.Current.Get(SplitKeys.OutputFolder),
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.OutputFolder = path;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => CloseHostWindow();

    private void CloseHostWindow()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }
}
