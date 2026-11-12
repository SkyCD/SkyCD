using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class CatalogFindWindow : Window
{
    public CatalogFindWindow()
    {
        InitializeComponent();
        Opened += (_, _) => QueryTextBox.Focus();
        Closed += (_, _) => (DataContext as CatalogFindDialogViewModel)?.Dispose();
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CatalogFindDialogViewModel vm && vm.SelectedResult is not null)
            vm.NavigateToResultCommand.Execute(vm.SelectedResult);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
