using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class PropertiesWindow : Window
{
    private PropertiesDialogViewModel? subscribedViewModel;

    public PropertiesWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not PropertiesWindow window || window.DataContext is not PropertiesDialogViewModel vm)
        {
            return;
        }

        if (ReferenceEquals(subscribedViewModel, vm))
        {
            return;
        }

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.CopyTextRequested -= OnCopyTextRequested;
        }

        subscribedViewModel = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.CopyTextRequested += OnCopyTextRequested;
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

    private async void OnCopyTextRequested(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && !string.IsNullOrEmpty(text))
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
