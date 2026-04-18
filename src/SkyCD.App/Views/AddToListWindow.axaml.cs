using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;
using System;

namespace SkyCD.App.Views;

public partial class AddToListWindow : Window
{
    public AddToListWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not AddToListWindow window)
        {
            return;
        }

        if (window.DataContext is AddToListDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is AddToListDialogViewModel vm &&
            e.PropertyName == nameof(AddToListDialogViewModel.DialogAccepted) &&
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
