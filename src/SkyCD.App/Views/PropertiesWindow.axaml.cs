using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class PropertiesWindow : Window
{
    public PropertiesWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not PropertiesWindow window)
        {
            return;
        }

        if (window.DataContext is PropertiesDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PropertiesDialogViewModel vm &&
            e.PropertyName == nameof(PropertiesDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
