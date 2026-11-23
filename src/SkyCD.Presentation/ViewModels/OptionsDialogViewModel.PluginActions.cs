using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Split button of the plugin page: Enable or Disable is the primary action, Uninstall sits behind the dropdown,
/// and the marketplace pane shows Install with the same dropdown. Uninstall deletes files and therefore runs
/// immediately after an explicit confirmation; Enable/Disable stay pending until the dialog is accepted.
/// </summary>
public partial class OptionsDialogViewModel
{
    private IPluginPackageUninstaller? pluginUninstaller;
    private OptionsPluginItem? pluginActionSource;
    private string? pendingUninstallPluginId;
    private string pendingUninstallPluginName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPluginActionAlert))]
    private string pluginActionAlertMessage = string.Empty;

    [ObservableProperty] private bool isPluginUninstallConfirmationVisible;
    [ObservableProperty] private bool isPluginActionBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPluginUninstallConfirmation))]
    private string pluginUninstallConfirmationPrompt = string.Empty;

    public bool HasPluginActionAlert => !string.IsNullOrWhiteSpace(PluginActionAlertMessage);

    public bool HasPluginUninstallConfirmation =>
        IsPluginUninstallConfirmationVisible && !string.IsNullOrWhiteSpace(PluginUninstallConfirmationPrompt);

    /// <summary>Disable/Enable label of the selected installed plugin.</summary>
    public string PluginPrimaryActionLabel => SelectedPlugin is { IsEnabled: true }
        ? LocalizationService.Current.Get(TextKeys.PluginDisableAction)
        : LocalizationService.Current.Get(TextKeys.PluginEnableAction);

    public string PluginPrimaryActionToolTip => SelectedPlugin is { IsEnabled: true }
        ? LocalizationService.Current.Get(TextKeys.PluginEnabledActionToolTip)
        : LocalizationService.Current.Get(TextKeys.PluginDisabledActionToolTip);

    /// <summary>Attaches the uninstaller used by the dropdown action of both plugin panes.</summary>
    public void SetPluginUninstaller(IPluginPackageUninstaller uninstaller)
    {
        pluginUninstaller = uninstaller ?? throw new ArgumentNullException(nameof(uninstaller));
    }

    [RelayCommand]
    private void ToggleSelectedPluginEnabled()
    {
        if (SelectedPlugin is not { } plugin)
        {
            return;
        }

        plugin.IsEnabled = !plugin.IsEnabled;
    }

    [RelayCommand]
    private void RequestPluginUninstall()
    {
        if (SelectedPlugin is { } plugin)
        {
            BeginPluginUninstall(plugin.Id, plugin.Name);
        }
    }

    [RelayCommand]
    private void RequestMarketplacePluginUninstall()
    {
        if (SelectedMarketplacePlugin is { CanUninstall: true, InstalledPluginId: { } pluginId } item)
        {
            BeginPluginUninstall(pluginId, item.Title);
        }
    }

    [RelayCommand]
    private void CancelPluginUninstall() => ClearPluginUninstallConfirmation();

    /// <summary>Deletes the plugin only after the user confirmed the destructive consequence.</summary>
    [RelayCommand]
    private async Task ConfirmPluginUninstallAsync()
    {
        if (pendingUninstallPluginId is not { } pluginId || pluginUninstaller is not { } uninstaller)
        {
            ClearPluginUninstallConfirmation();
            return;
        }

        var displayName = pendingUninstallPluginName;
        ClearPluginUninstallConfirmation();
        IsPluginActionBusy = true;
        PluginActionAlertMessage = string.Empty;
        try
        {
            // Deletion is not cancellable halfway: a cancelled recursive delete would leave partial files.
            var result = await uninstaller.UninstallAsync(pluginId, CancellationToken.None);
            if (!result.IsSuccess)
            {
                PluginActionAlertMessage = LocalizationService.Current.Format(TextKeys.PluginUninstallFailed,
                    displayName, DescribeUninstallFailure(result.FailureReason));
                return;
            }

            ApplyUninstalledPlugin(pluginId);
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(GetType(), "uninstall", exception);
            PluginActionAlertMessage = LocalizationService.Current.Format(TextKeys.PluginUninstallFailed,
                displayName, exception.Message);
        }
        finally
        {
            IsPluginActionBusy = false;
        }
    }

    // The dialog keeps its own plugin instances; the folder is gone, so the item and its pressed state must go too.
    private void ApplyUninstalledPlugin(string pluginId)
    {
        pendingPluginStates.Remove(pluginId);
        disabledPluginIds.Remove(pluginId);

        var installed = Plugins.FirstOrDefault(plugin =>
            string.Equals(plugin.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (installed is not null)
        {
            Plugins.Remove(installed);
            FilteredPlugins.Remove(installed);
            if (ReferenceEquals(SelectedPlugin, installed))
            {
                SelectedPlugin = FilteredPlugins.FirstOrDefault();
            }

            NotifyPluginsPageChanged();
        }

        foreach (var item in allMarketplaceItems.Where(item =>
                     string.Equals(item.InstalledPluginId, pluginId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            item.ApplyResult(PluginInstallResult.Success(PluginInstallState.NotInstalled(pluginId)));
        }

        OnPropertyChanged(nameof(HasAvailableMarketplaceUpdates));
        UpdateAllPluginsCommand.NotifyCanExecuteChanged();

        // Discovery keeps its snapshot until the host rescans the plugin root.
        RefreshPluginsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BeginPluginUninstall(string pluginId, string displayName)
    {
        if (pluginUninstaller is null)
        {
            return;
        }

        IsInstallConfirmationVisible = false;
        pendingUninstallPluginId = pluginId;
        pendingUninstallPluginName = displayName;
        PluginActionAlertMessage = string.Empty;
        PluginUninstallConfirmationPrompt =
            LocalizationService.Current.Format(TextKeys.PluginUninstallConfirmation, displayName);
        IsPluginUninstallConfirmationVisible = true;
    }

    private void ClearPluginUninstallConfirmation()
    {
        if (pendingUninstallPluginId is null && !IsPluginUninstallConfirmationVisible)
        {
            return;
        }

        pendingUninstallPluginId = null;
        pendingUninstallPluginName = string.Empty;
        PluginUninstallConfirmationPrompt = string.Empty;
        IsPluginUninstallConfirmationVisible = false;
    }

    private static string DescribeUninstallFailure(PluginInstallFailureReason reason) => reason switch
    {
        PluginInstallFailureReason.InvalidPluginId => LocalizationService.Current.Get(TextKeys.PluginUninstallInvalidId),
        PluginInstallFailureReason.UnsafeInstallPath =>
            LocalizationService.Current.Get(TextKeys.PluginUninstallUnsafePath),
        PluginInstallFailureReason.NotCatalogOwned =>
            LocalizationService.Current.Get(TextKeys.PluginUninstallNotCatalogOwned),
        _ => LocalizationService.Current.Get(TextKeys.PluginUninstallFilesInUse)
    };

    private void NotifyPluginActionChanged()
    {
        OnPropertyChanged(nameof(PluginPrimaryActionLabel));
        OnPropertyChanged(nameof(PluginPrimaryActionToolTip));
    }

    private void OnPluginActionSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptionsPluginItem.IsEnabled))
        {
            NotifyPluginActionChanged();
        }
    }

    /// <summary>
    /// Watches the selected plugin so the primary label follows Enable/Disable without extra hooks in the
    /// discovered-plugin pages, and drops an open uninstall confirmation when the selection changes.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(SelectedPlugin))
        {
            if (pluginActionSource is not null)
            {
                pluginActionSource.PropertyChanged -= OnPluginActionSourceChanged;
            }

            pluginActionSource = SelectedPlugin;
            if (pluginActionSource is not null)
            {
                pluginActionSource.PropertyChanged += OnPluginActionSourceChanged;
            }

            NotifyPluginActionChanged();
            ClearPluginUninstallConfirmation();
        }
        else if (e.PropertyName == nameof(SelectedMarketplacePlugin))
        {
            ClearPluginUninstallConfirmation();
        }
    }

    partial void OnIsPluginUninstallConfirmationVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(HasPluginUninstallConfirmation));
}
