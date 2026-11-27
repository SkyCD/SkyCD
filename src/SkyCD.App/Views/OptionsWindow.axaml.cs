using SkyCD.UI.Localization;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class OptionsWindow : Window
{
    private const double TargetWidth = 1024;
    private const double TargetHeight = 768;
    private const double ScreenUsageFactor = 0.9;
    private bool hasOpened;

    public OptionsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Opened += OnLoadPages;
        Closed += (_, _) =>
        {
            if (DataContext is OptionsDialogViewModel vm)
            {
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.Dispose();
            }
        };
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
            vm.CollectionsPage?.WatchCatalogChanges(action => Dispatcher.UIThread.Post(action));
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is OptionsDialogViewModel vm &&
            e.PropertyName == nameof(OptionsDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            Close(true);
        }
        else if (e.PropertyName == nameof(OptionsDialogViewModel.ShowPluginSettingsSection))
        {
            await LoadPluginPageIfVisibleAsync();
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnPluginPathFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel vm)
        {
            // Leaving the field commits the typed folder; typing itself never scans.
            vm.RescanPluginPathCommand.Execute(null);
        }
    }

    private void OnInstalledPluginsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel { SelectedPlugin: not null } vm)
        {
            vm.ShowInstalledPluginsCommand.Execute(null);
        }
    }

    private void OnMarketplacePluginsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel { SelectedMarketplacePlugin: not null } vm)
        {
            vm.ShowMarketplacePluginsCommand.Execute(null);
        }
    }

    private void OnCollectionMemberDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel { CollectionsPage: { } collections } &&
            collections.NavigateToSelectedCommand.CanExecute(null))
        {
            collections.NavigateToSelectedCommand.Execute(null);
        }
    }

    private void OnCreateFirstCollectionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel { CollectionsPage: { } collections } &&
            collections.AddCollectionCommand.CanExecute(null))
        {
            collections.AddCollectionCommand.Execute(null);
        }
    }

    private void OnToolbarCommandDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel { Toolbar: { AddCommand: var add } } && add.CanExecute(null))
        {
            add.Execute(null);
        }
    }

    private async void OnLoadPages(object? sender, EventArgs e)
    {
        hasOpened = true;
        await LoadPluginPageIfVisibleAsync();
    }

    private async Task LoadPluginPageIfVisibleAsync()
    {
        if (hasOpened && DataContext is OptionsDialogViewModel { ShowPluginSettingsSection: true, SelectedPluginPage: { } page })
            await page.LoadAsync();
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
            vm.McpAlertMessage = LocalizationService.Current.Get(TextKeys.ClipboardIsUnavailable);
            return;
        }

        vm.McpCopyTooltip = LocalizationService.Current.Get(TextKeys.Copied2);
        await clipboard.SetTextAsync(vm.McpBaseUrl);
        vm.McpAlertMessage = LocalizationService.Current.Format(TextKeys.CopiedMCPURL, vm.McpBaseUrl);

        await Task.Delay(4000);
        if (DataContext is OptionsDialogViewModel currentVm)
        {
            currentVm.McpCopyTooltip = LocalizationService.Current.Get(TextKeys.CopyURL);
            currentVm.McpAlertMessage = string.Empty;
        }
    }

}
