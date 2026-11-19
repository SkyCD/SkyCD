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
        ResultsList.AddHandler(InputElement.KeyDownEvent, OnResultsKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => (DataContext as AdvancedCatalogFindDialogViewModel)?.Dispose();
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        OpenResult();
    }

    private void OnResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = OpenResult();
    }

    private void OnOpenResultClicked(object? sender, RoutedEventArgs e) => OpenResult();

    private bool OpenResult()
    {
        if (DataContext is AdvancedCatalogFindDialogViewModel vm &&
            vm.NavigateToResultCommand.CanExecute(vm.SelectedResult))
        {
            vm.NavigateToResultCommand.Execute(vm.SelectedResult);
            Close(true);
            return true;
        }

        return false;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
