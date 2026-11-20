using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class ToolbarCustomizationWindow : Window
{
    private ToolbarCustomizationViewModel? subscribedViewModel;

    public ToolbarCustomizationWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        subscribedViewModel = (sender as ToolbarCustomizationWindow)?.DataContext as ToolbarCustomizationViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ToolbarCustomizationViewModel vm &&
            e.PropertyName == nameof(ToolbarCustomizationViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }

    private void OnCommandDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ToolbarCustomizationViewModel { AddCommand: var add } && add.CanExecute(null))
        {
            add.Execute(null);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
