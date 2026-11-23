using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class MergeLocationsWindow : Window
{
    public MergeLocationsWindow()
    {
        InitializeComponent();
    }

    public MergeLocationsWindow(MergeLocationsDialogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e) =>
        Close((DataContext as MergeLocationsDialogViewModel)?.SelectedDestination);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
