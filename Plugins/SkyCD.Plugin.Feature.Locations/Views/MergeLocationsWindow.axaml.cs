using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.Feature.Locations.Views;

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
