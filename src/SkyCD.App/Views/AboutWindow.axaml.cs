using System;
using System.ComponentModel;
using Avalonia.Controls;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not AboutWindow window) return;

        if (window.DataContext is AboutDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AboutDialogViewModel vm &&
            e.PropertyName == nameof(AboutDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
            Close(true);
    }
}