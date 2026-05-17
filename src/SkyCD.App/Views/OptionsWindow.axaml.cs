using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
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
        AddHandler(DragDrop.DragOverEvent, OnStatusIconSquareDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, OnStatusIconSquareDragLeave, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DropEvent, OnStatusIconSquareDrop, RoutingStrategies.Bubble, true);
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
        await CopyMcpUrlAsync();
    }

    private async void OnMcpUrlDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }

        await CopyMcpUrlAsync();
    }

    private async Task CopyMcpUrlAsync()
    {
        if (DataContext is not OptionsDialogViewModel vm)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            vm.McpAlertMessage = "Clipboard is unavailable.";
            return;
        }

        vm.McpCopyTooltip = "Copied";
        await clipboard.SetTextAsync(vm.McpBaseUrl);
        vm.McpAlertMessage = $"Copied MCP URL: {vm.McpBaseUrl}";

        await Task.Delay(4000);
        if (DataContext is OptionsDialogViewModel currentVm)
        {
            currentVm.McpCopyTooltip = "Copy URL";
            currentVm.McpAlertMessage = string.Empty;
        }
    }

    private async void OnStatusIconSquarePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not StatusVariantItemViewModel item)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select status icon",
            AllowMultiple = false
        });

        var localPath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        item.IconGlyph = localPath;
        item.IsDropHintVisible = false;
    }

    private void OnStatusIconSquareDragOver(object? sender, DragEventArgs e)
    {
        if (!TryGetStatusIconDropBorder(e.Source, out var border) ||
            border is null ||
            border.Tag is not StatusVariantItemViewModel item)
        {
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        item.IsDropHintVisible = true;
    }

    private void OnStatusIconSquareDragLeave(object? sender, RoutedEventArgs e)
    {
        if (TryGetStatusIconDropBorder(e.Source, out var border) &&
            border is not null &&
            border.Tag is StatusVariantItemViewModel item)
        {
            item.IsDropHintVisible = false;
        }
    }

    private void OnStatusIconSquareDrop(object? sender, DragEventArgs e)
    {
        if (!TryGetStatusIconDropBorder(e.Source, out var border) ||
            border is null ||
            border.Tag is not StatusVariantItemViewModel item)
        {
            return;
        }

        item.IsDropHintVisible = false;
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            e.Handled = true;
            return;
        }

        item.IconGlyph = path;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private static bool TryGetStatusIconDropBorder(object? source, out Border? border)
    {
        border = source as Border;
        while (border is null && source is StyledElement styledElement)
        {
            source = styledElement.Parent;
            border = source as Border;
        }

        return border is not null && border.Classes.Contains("status-icon-drop");
    }
}
