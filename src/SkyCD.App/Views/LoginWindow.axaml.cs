using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;
using System;
using System.ComponentModel;

namespace SkyCD.App.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not LoginWindow window)
        {
            return;
        }

        if (window.DataContext is LoginDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is LoginDialogViewModel vm &&
            e.PropertyName == nameof(LoginDialogViewModel.DialogAccepted) &&
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
