using System;
using System.ComponentModel;
using Avalonia.Controls;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class LocationsWindow : Window
{
    public LocationsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not LocationsWindow window)
        {
            return;
        }

        if (window.DataContext is LocationsDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is LocationsDialogViewModel vm &&
            e.PropertyName == nameof(LocationsDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }
}
