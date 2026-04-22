using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not OptionsWindow window) return;

        if (window.DataContext is OptionsDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is OptionsDialogViewModel vm &&
            e.PropertyName == nameof(OptionsDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
            Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}