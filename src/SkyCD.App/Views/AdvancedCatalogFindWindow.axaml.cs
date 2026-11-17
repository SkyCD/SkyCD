using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AdvancedCatalogFindWindow : Window
{
    public AdvancedCatalogFindWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as AdvancedCatalogFindDialogViewModel)?.Dispose();
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AdvancedCatalogFindDialogViewModel vm &&
            vm.NavigateToResultCommand.CanExecute(vm.SelectedResult))
        {
            vm.NavigateToResultCommand.Execute(vm.SelectedResult);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
