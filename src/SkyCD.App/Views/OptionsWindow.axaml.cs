using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class OptionsWindow : Window
{
    private const double TargetWidth = 1024;
    private const double TargetHeight = 768;
    private const double ScreenUsageFactor = 0.9;

    public OptionsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not OptionsWindow window)
        {
            return;
        }

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
        {
            Close(true);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var screens = Screens;
        var screen = screens.ScreenFromVisual(this) ?? screens.Primary;
        if (screen is null)
        {
            Width = TargetWidth;
            Height = TargetHeight;
            return;
        }

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var maxWidth = (screen.WorkingArea.Width / scaling) * ScreenUsageFactor;
        var maxHeight = (screen.WorkingArea.Height / scaling) * ScreenUsageFactor;

        Width = Math.Min(TargetWidth, maxWidth);
        Height = Math.Min(TargetHeight, maxHeight);
    }

    private async void OnCopyMcpUrlClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OptionsDialogViewModel vm)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            vm.InfoMessage = "Clipboard is unavailable.";
            return;
        }

        await clipboard.SetTextAsync(vm.McpBaseUrl);
        vm.InfoMessage = $"Copied MCP URL: {vm.McpBaseUrl}";
    }
}
