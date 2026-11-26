using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

/// <summary>
/// The export form. The view owns the window lifetime; the view model owns the format choice.
/// The host shows the target picker and runs the write after the dialog closes with a format.
/// </summary>
public partial class ExportWindow : Window
{
    private ExportDialogViewModel? boundViewModel;

    public ExportWindow() => InitializeComponent();

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

        boundViewModel = DataContext as ExportDialogViewModel;
        if (boundViewModel is not null)
        {
            boundViewModel.CloseRequested += CloseWindow;
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
