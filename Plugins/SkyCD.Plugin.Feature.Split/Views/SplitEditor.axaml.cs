using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SkyCD.Plugin.Feature.Split.Views;

/// <summary>
/// The split form shown inside the host dialog window. The host owns the window, so the view reaches it only to
/// close it once the parts are written or the user dismisses the form.
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

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
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
            Title = "Choose where the part files are written",
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            viewModel.OutputFolder = path;
        }
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => CloseHostWindow();

    private void CloseHostWindow()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }
}
