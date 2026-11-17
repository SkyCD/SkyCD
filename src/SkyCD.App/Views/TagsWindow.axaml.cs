using System;
using System.ComponentModel;
using Avalonia.Controls;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class TagsWindow : Window
{
    public TagsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not TagsWindow window)
        {
            return;
        }

        if (window.DataContext is TagsDialogViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TagsDialogViewModel vm &&
            e.PropertyName == nameof(TagsDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
    }
}
