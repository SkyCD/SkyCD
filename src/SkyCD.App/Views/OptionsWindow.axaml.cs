using SkyCD.UI.Localization;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.App.Views;

public partial class OptionsWindow : Window
{
    private const double TargetWidth = 1024;
    private const double TargetHeight = 768;
    private const double ScreenUsageFactor = 0.9;
    private readonly CancellationTokenSource acpLifetime = new();
    private bool hasOpened;
    private bool startedAcpLoad;
    private bool startedAiProviderLoad;

    public IAcpAgentSettingsCapability? AcpSettingsCapability { get; init; }

    public IAiProviderSettingsCapability? AiProviderSettingsCapability { get; init; }

    public OptionsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Opened += OnLoadAcpAgents;
        Opened += OnLoadAiProviderSettings;
        Closed += (_, _) =>
        {
            acpLifetime.Cancel();
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
        else if (e.PropertyName == nameof(OptionsDialogViewModel.ShowAcpSection))
        {
            await LoadAcpAgentsIfVisibleAsync();
        }
        else if (e.PropertyName is nameof(OptionsDialogViewModel.ShowAiDescriptionSection)
                 or nameof(OptionsDialogViewModel.ShowAiProviderSection))
        {
            LoadAiProviderSettingsIfVisible();
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnToolbarCommandDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OptionsDialogViewModel { Toolbar: { AddCommand: var add } } && add.CanExecute(null))
        {
            add.Execute(null);
        }
    }

    private async void OnBrowseAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OptionsDialogViewModel { CanChooseAcpAgent: true } vm ||
            sender is not Button { DataContext: AcpAgentChoice { ExpectedExecutable: { } expected } agent }) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Current.Format(UiKeys.AcpFindExecutable, expected),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(expected) { Patterns = [expected] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.ChooseLocalAcpAgent(agent, path);
    }

    private async void OnLoadAcpAgents(object? sender, EventArgs e)
    {
        hasOpened = true;
        LoadAiProviderSettingsIfVisible();
        await LoadAcpAgentsIfVisibleAsync();
        await LoadPluginPageIfVisibleAsync();
    }

    private async Task LoadPluginPageIfVisibleAsync()
    {
        if (hasOpened && DataContext is OptionsDialogViewModel { ShowPluginSettingsSection: true, SelectedPluginPage: { } page })
            await page.LoadAsync();
    }

    private void OnLoadAiProviderSettings(object? sender, EventArgs e)
    {
        hasOpened = true;
        LoadAiProviderSettingsIfVisible();
    }

    private void LoadAiProviderSettingsIfVisible()
    {
        if (!hasOpened || startedAiProviderLoad || DataContext is not OptionsDialogViewModel { IsAiDescriptionAvailable: true } vm)
        {
            return;
        }

        startedAiProviderLoad = true;
        vm.LoadAiProviderSettings(AiProviderSettingsCapability);
    }

    private async Task LoadAcpAgentsIfVisibleAsync()
    {
        if (!hasOpened || startedAcpLoad || DataContext is not OptionsDialogViewModel { ShowAcpSection: true } vm)
            return;

        startedAcpLoad = true;
        await vm.LoadAcpSettingsAsync(AcpSettingsCapability, refreshRegistry: true, acpLifetime.Token);
    }

    private async void OnInstallAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AcpAgentChoice agent } || DataContext is not OptionsDialogViewModel vm)
            return;

        var error = agent.IsManaged
            ? await vm.UpdateAcpAgentAsync(agent, acpLifetime.Token)
            : await vm.InstallAcpAgentAsync(agent, acpLifetime.Token);
        await ShowAcpOperationErrorAsync(agent.IsManaged ? UiKeys.AcpUpdateErrorTitle : UiKeys.AcpInstallErrorTitle,
            agent.Name, error);
    }

    private async void OnUninstallAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AcpAgentChoice agent } || DataContext is not OptionsDialogViewModel vm)
            return;

        var error = await vm.UninstallAcpAgentAsync(agent, acpLifetime.Token);
        await ShowAcpOperationErrorAsync(UiKeys.AcpUninstallErrorTitle, agent.Name, error);
    }

    private async Task ShowAcpOperationErrorAsync(string title, string agentName, string? error)
    {
        if (error is null || acpLifetime.IsCancellationRequested) return;

        var close = new Button
        {
            Content = LocalizationService.Current.Get(UiKeys.Ok),
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        var dialog = new Window
        {
            Title = LocalizationService.Current.Format(title, agentName),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            CanMinimize = false,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 240,
                        Content = new SelectableTextBlock
                        {
                            Text = error,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private void OnSelectAcpAgentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AcpAgentChoice { IsInstalled: true } agent } &&
            DataContext is OptionsDialogViewModel { CanChooseAcpAgent: true } vm)
            vm.SelectAcpAgent(agent);
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
