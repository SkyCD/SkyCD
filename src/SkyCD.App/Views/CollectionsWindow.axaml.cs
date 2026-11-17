using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class CollectionsWindow : Window
{
    private CollectionsDialogViewModel? subscribedViewModel;

    public CollectionsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        subscribedViewModel = (sender as CollectionsWindow)?.DataContext as CollectionsDialogViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is CollectionsDialogViewModel vm &&
            e.PropertyName == nameof(CollectionsDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }

    private void OnMembersDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CollectionsDialogViewModel vm &&
            vm.NavigateToSelectedCommand.CanExecute(null))
        {
            vm.NavigateToSelectedCommand.Execute(null);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
