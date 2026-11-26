using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.App.Icons;
using SkyCD.Core.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Plugins;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void OnOptionsRequested(object? sender, OptionsDialogRequestedEventArgs e) =>
        _ = RunUiActionAsync(() => OptionsRequestedAsync(e));

    internal Task ShowContributedOptionsAsync(string categoryId, Window owner) =>
        DataContext is MainWindowViewModel viewModel
            ? RunUiActionAsync(() => OptionsRequestedAsync(viewModel.CreateOptionsRequest(categoryId), owner))
            : Task.CompletedTask;

    private async Task OptionsRequestedAsync(OptionsDialogRequestedEventArgs e, Window? owner = null)
    {
        var options = LoadAppOptions();
        var pluginPath = options.PluginPath;

        e.Dialog.InitializePluginPath(pluginPath);
        e.Dialog.SetPluginRepositories(options.PluginRepositorySources);
        e.Dialog.IsMcpServerEnabled = options.IsMcpServerEnabled;
        e.Dialog.McpPort = options.McpPort;
        e.Dialog.IsMcpStatusIconVisible = options.IsMcpStatusIconVisible;
        e.Dialog.SetStatusVariants(statusVariantRepository.GetOrdered());
        e.Dialog.SetCustomProperties(options.CustomProperties);
        e.Dialog.SetDetailsColumns(DataContext is MainWindowViewModel mainViewModel
            ? mainViewModel.GetDetailsColumnSettings()
            : options.Browser.DetailsColumns);
        LoadScanOptionsIntoDialog(e.Dialog, options);
        e.Dialog.SetMediaEjectionSupported(mediaEjector.IsPlatformSupported);
        e.Dialog.SetAiDescriptionAvailable(pluginManager.GetCapabilities<IGeneratedDescriptionCapability>().Any());
        e.Dialog.SetAiDescriptionImplementations(
            ServiceProvider.Resolve<DescriptionExtensionManager>().Implementations,
            options.AiDescriptionImplementation);
        e.Dialog.SetAiScanExclusions(aiScanExclusionRepository.GetOrdered());
        e.Dialog.SetAccountSessions(pluginManager.GetCapabilities<IAccountSession>());
        if (DataContext is MainWindowViewModel catalogViewModel)
        {
            e.Dialog.SetCatalogAccountFolders(catalogViewModel.GetAllCatalogEntries());
        }
        e.Dialog.AiScanMaxFileSizeMegabytes = ToAiScanMaxFileSizeMegabytes(options.AiScanMaxFileSizeBytes);
        // Discovery must never block opening Options; an unreachable bridge keeps the stored selection intact.
        e.Dialog.InitializeMcpTools(options.ExposedMcpTools);
        try
        {
            var mcpTools = Application.Current is App app ? await app.GetMcpToolsAsync() : [];
            e.Dialog.SetMcpTools(mcpTools.Select(tool => (tool.Name, tool.CommandPath)), options.ExposedMcpTools);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Leave the page empty; saving preserves the existing selection.
        }
        if (DataContext is MainWindowViewModel toolbarViewModel)
        {
            e.Dialog.SetToolbarCustomization(toolbarViewModel.ToolbarCommands(), toolbarViewModel.ToolbarEntries);
        }
        e.Dialog.SetPluginMarketplace(pluginMarketplaceService);
        e.Dialog.SetPluginUninstaller(pluginUninstaller);
        e.Dialog.SetPluginPathBrowser(() => RunUiActionAsync(() => e.Dialog.BrowsePluginPathAsync(PickPluginFolderAsync)));
        e.Dialog.RefreshPluginsRequested += OnRefreshPluginsRequested;
        e.Dialog.PluginPathScanRequested += OnPluginPathScanRequested;
        e.Dialog.ResetStatusVariantsRequested += OnResetStatusVariantsRequested;
        e.Dialog.ResetScanExclusionRulesRequested += OnResetScanExclusionRulesRequested;
        e.Dialog.ResetAiScanExclusionsRequested += OnResetAiScanExclusionsRequested;
        e.Dialog.ToolbarLayoutApplied += OnOptionsToolbarLayoutApplied;
        try
        {
            RefreshPlugins(e.Dialog);
            e.Dialog.RestoreSettingCategory(options.OptionsCategoryId, options.OptionsTabIndex);
            if (!string.IsNullOrWhiteSpace(e.PreselectCategoryId))
            {
                e.Dialog.SettingsSearchText = string.Empty;
                e.Dialog.SelectedSettingCategory = e.Dialog.SettingCategories.FirstOrDefault(category =>
                    string.Equals(category.Id, e.PreselectCategoryId, StringComparison.Ordinal));
            }

            var dialog = new OptionsWindow
            {
                DataContext = e.Dialog,
                AcpSettingsCapability = pluginManager.GetCapabilities<IAcpAgentSettingsCapability>().FirstOrDefault(),
                AiProviderSettingsCapability = pluginManager.GetCapabilities<IAiProviderSettingsCapability>().FirstOrDefault()
            };

            bool? accepted = false;
            try
            {
                accepted = await dialog.ShowDialog<bool?>(owner ?? this);
            }
            finally
            {
                e.Dialog.CompleteLanguageSelection(accepted == true);
            }
            if (accepted == true)
            {
                var pluginStates = e.Dialog.Plugins
                    .Select(static plugin => (plugin.Id, plugin.IsEnabled))
                    .ToArray();
                var repositorySources = e.Dialog.GetPluginRepositories();

                options.CustomProperties = e.Dialog.GetCustomProperties();
                options.PluginPath = e.Dialog.PluginPath;
                options.PluginRepositorySources = repositorySources.Count == 0 ? null : repositorySources.ToList();
                options.IsMcpServerEnabled = e.Dialog.IsMcpServerEnabled;
                options.McpPort = e.Dialog.McpPort;
                options.IsMcpStatusIconVisible = e.Dialog.IsMcpStatusIconVisible;
                options.ExposedMcpTools = e.Dialog.GetExposedMcpTools()?.ToList();
                options.Language = e.Dialog.SelectedLanguage.CultureCode;
                options.OptionsTabIndex = Math.Max(0, e.Dialog.SelectedSettingCategory?.BuiltInIndex ?? 0);
                options.OptionsCategoryId = e.Dialog.SelectedSettingCategory?.Id;
                options.Browser.DetailsColumns = e.Dialog.GetDetailsColumns().ToList();
                options.AiScanMaxFileSizeBytes = e.Dialog.AiScanMaxFileSizeMegabytes * 1024L * 1024L;
                options.AiDescriptionImplementation = e.Dialog.SelectedAiDescriptionImplementation?.Id
                                                      ?? options.AiDescriptionImplementation;
                if (e.Dialog.GetToolbarLayout() is { } toolbarLayout)
                {
                    options.Toolbar = toolbarLayout.ToList();
                }

                SaveScanOptionsFromDialog(e.Dialog, options);
                SaveAppOptions(options);
                await ApplyPluginRepositorySourcesAsync(repositorySources);
                statusVariantRepository.ReplaceAll(e.Dialog.GetStatusVariants());
                scanExclusionRuleRepository.ReplaceAll(e.Dialog.GetScanExclusionRules());
                aiScanExclusionRepository.ReplaceAll(e.Dialog.GetAiScanExclusions());
                // A marketplace install staged in this dialog is invisible to discovery until it is applied, so
                // the availability refresh below would otherwise miss a plugin installed without a restart.
                try
                {
                    await pluginRuntimeSynchronizer.ApplyPendingInstallsAsync();
                }
                catch (Exception exception)
                {
                    // A broken package must not crash the app from the options workflow; the remaining
                    // settings still apply and the failed install keeps its state for the plugin list.
                    logger.LogWarning(exception, "Applying staged plugin installs failed; the active plugin set is unchanged.");
                }
                pluginManager.SavePluginEnabledStates(pluginStates);
                SyncPluginRuntimeState();
                ApplyLanguage(options.Language);
                if (Application.Current is App app)
                {
                    await app.ApplyMcpSettingsAsync();
                }
                RefreshMcpStatusIndicator();
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.SetStatusVariants(statusVariantRepository.GetOrdered());
                    vm.ApplyDetailsColumnSettings(options.Browser.DetailsColumns);
                }

                // Trigger UI refresh to apply new language
                InvalidateVisual();
            }
            else
            {
                // A rescan performed while the dialog was open changed live plugin state; put the stored
                // folder back so Cancel leaves the running app on the persisted path.
                e.Dialog.RevertPluginPathPreview();
            }

            e.Complete(accepted == true, e.Dialog.PluginPath, e.Dialog.SelectedLanguage.Name);
        }
        finally
        {
            e.Dialog.SetPluginPathBrowser(null);
            e.Dialog.RefreshPluginsRequested -= OnRefreshPluginsRequested;
            e.Dialog.PluginPathScanRequested -= OnPluginPathScanRequested;
            e.Dialog.ResetStatusVariantsRequested -= OnResetStatusVariantsRequested;
            e.Dialog.ResetScanExclusionRulesRequested -= OnResetScanExclusionRulesRequested;
            e.Dialog.ResetAiScanExclusionsRequested -= OnResetAiScanExclusionsRequested;
            e.Dialog.ToolbarLayoutApplied -= OnOptionsToolbarLayoutApplied;
        }
    }

    private async Task ApplyPluginRepositorySourcesAsync(IReadOnlyList<string> sources)
    {
        try
        {
            await pluginMarketplaceService.ApplyRepositorySourcesAsync(sources
                .Select(static url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
                .OfType<Uri>()
                .ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to apply plugin repository sources.");
        }
    }

    private void OnOptionsToolbarLayoutApplied(object? sender, ToolbarLayoutAppliedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ApplyToolbarLayout(e.Layout);
        }
    }

    private void LoadScanOptionsIntoDialog(OptionsDialogViewModel dialog, AppOptionsDocument options)
    {
        dialog.SetScanOptions(new ScanSettingsSnapshot(
            options.SkipSymbolicLinks,
            options.HiddenSystemFilePolicy,
            options.SkipRecycleBins,
            options.SkipWellKnownSystemFolders,
            options.SkipEmptyFolders,
            options.SkipOnlineOnlyCloudFiles,
            options.IncludeExtendedInfo,
            options.ShowScanErrorsAfterScan,
            options.SaveScanErrorsToComments,
            options.EjectMediaAfterScan,
            options.ActivateMainWindowAfterScan,
            options.PlaySoundAfterScan,
            options.ScanConcurrencyMode,
            options.ScanConcurrencyManualWorkers,
            options.ScanConcurrencyCpuSharePercent,
            options.ScanConcurrencyLoadShrinkPercent,
            options.ScanConcurrencyLoadSuspendPercent));
        dialog.SetScanExclusionRules(scanExclusionRuleRepository.GetOrdered());
    }

    private void SaveScanOptionsFromDialog(OptionsDialogViewModel dialog, AppOptionsDocument options)
    {
        var scanSettings = dialog.GetScanOptions();
        options.SkipSymbolicLinks = scanSettings.SkipSymbolicLinks;
        options.HiddenSystemFilePolicy = scanSettings.HiddenSystemFilePolicy;
        options.SkipRecycleBins = scanSettings.SkipRecycleBins;
        options.SkipWellKnownSystemFolders = scanSettings.SkipWellKnownSystemFolders;
        options.SkipEmptyFolders = scanSettings.SkipEmptyFolders;
        options.SkipOnlineOnlyCloudFiles = scanSettings.SkipOnlineOnlyCloudFiles;
        options.IncludeExtendedInfo = scanSettings.IncludeExtendedInfo;
        options.ShowScanErrorsAfterScan = scanSettings.ShowScanErrorsAfterScan;
        options.SaveScanErrorsToComments = scanSettings.SaveScanErrorsToComments;
        options.EjectMediaAfterScan = scanSettings.EjectMediaAfterScan;
        options.ActivateMainWindowAfterScan = scanSettings.ActivateMainWindowAfterScan;
        options.PlaySoundAfterScan = scanSettings.PlaySoundAfterScan;
        options.ScanConcurrencyMode = scanSettings.ScanConcurrencyMode;
        options.ScanConcurrencyManualWorkers = scanSettings.ScanConcurrencyManualWorkers;
        options.ScanConcurrencyCpuSharePercent = scanSettings.ScanConcurrencyCpuSharePercent;
        options.ScanConcurrencyLoadShrinkPercent = scanSettings.ScanConcurrencyLoadShrinkPercent;
        options.ScanConcurrencyLoadSuspendPercent = scanSettings.ScanConcurrencyLoadSuspendPercent;
    }


    private async Task<string?> PickPluginFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current.Get(TextKeys.SelectPlugInDirectory),
            AllowMultiple = false
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    private void OnRefreshPluginsRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        RefreshPlugins(dialogVm);
    }

    /// <summary>Handles the folder field's typing pause, Enter and lost focus.</summary>
    private void OnPluginPathScanRequested(object? sender, EventArgs e)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        // The debounce expires on a pool thread, while the list it rebuilds is bound to the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshPlugins(dialogVm);
            return;
        }

        Dispatcher.UIThread.Post(() => RefreshPlugins(dialogVm));
    }

    private void OnResetStatusVariantsRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(() => ResetStatusVariantsRequestedAsync(sender));

    private async Task ResetStatusVariantsRequestedAsync(object? sender)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var confirmed = await ShowResetStatusesPromptAsync();
        if (!confirmed)
        {
            return;
        }

        statusVariantRepository.ReplaceAll(statusVariantRepository.CreateDefaultEntries());
        dialogVm.SetStatusVariants(statusVariantRepository.GetOrdered());
    }

    private void OnResetScanExclusionRulesRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(() => ResetScanExclusionRulesRequestedAsync(sender));

    private async Task ResetScanExclusionRulesRequestedAsync(object? sender)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var confirmed = await ShowResetScanRulesPromptAsync();
        if (!confirmed)
        {
            return;
        }

        scanExclusionRuleRepository.ReplaceAll(scanExclusionRuleRepository.CreateDefaultEntries());
        dialogVm.SetScanExclusionRules(scanExclusionRuleRepository.GetOrdered());
    }

    private void OnResetAiScanExclusionsRequested(object? sender, EventArgs e) =>
        _ = RunUiActionAsync(() => ResetAiScanExclusionsRequestedAsync(sender));

    private async Task ResetAiScanExclusionsRequestedAsync(object? sender)
    {
        if (sender is not OptionsDialogViewModel dialogVm)
        {
            return;
        }

        var confirmed = await ShowResetMimeExclusionsPromptAsync();
        if (!confirmed)
        {
            return;
        }

        aiScanExclusionRepository.ReplaceAll(aiScanExclusionRepository.CreateDefaultEntries());
        dialogVm.SetAiScanExclusions(aiScanExclusionRepository.GetOrdered());
    }

    private void RefreshPlugins(OptionsDialogViewModel dialogVm)
    {
        try
        {
            RefreshPluginsCore(dialogVm);
        }
        catch (Exception exception)
        {
            // Keep plugin scan failures on the existing plugin error path.
            logger.LogWarning(exception, "Refreshing the plugin list failed; the Options dialog remains usable.");
        }
    }

    private void RefreshPluginsCore(OptionsDialogViewModel dialogVm)
    {
        dialogVm.CapturePluginStates();
        pluginManager.Discover(dialogVm.PluginPath, hostVersionProvider.Current);
        // Discovery builds fresh capability instances, so the host APIs are re-injected before the
        // contributed Options pages use them — otherwise a page like Locations fails with a plugin
        // error every time the list refreshes.
        pluginRuntimeSynchronizer.ReinjectHostApis();
        // The runtime now reflects this folder: it is the path Cancel has to restore.
        dialogVm.MarkPluginPathScanned(dialogVm.PluginPath);
        var descriptors = pluginManager.GetPluginDescriptors();
        var discoveredById = pluginManager.AvailablePlugins
            .ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase);
        var installStates = pluginMarketplaceService.GetInstallStates();
        var configurationManager = ServiceProvider.Resolve<PluginConfigurationManager>();

        var plugins = descriptors
            .Select(descriptor => CreateOptionsPluginItem(descriptor, discoveredById, installStates))
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        dialogVm.SetPlugins(plugins);
        dialogVm.SetPluginOptionsPages(pluginManager.AvailablePlugins.SelectMany(plugin =>
            plugin.Capabilities.OfType<IOptionsPageCapability>()
                .Select(capability => new OptionsPageProvider(plugin.Id, plugin.Name, capability))));
        dialogVm.SetPluginConfigurations(configurationManager, PluginConfigurationTimeout);
    }

    /// <summary>
    /// Combines the persisted descriptor (enabled state, stale plugins) with the freshly discovered
    /// assembly metadata so the Additional Info tab can show links, version, size and update source.
    /// </summary>
    /// <summary>Render size of the plugin bitmaps in the installed list rows.</summary>
    private const int PluginListIconSize = 32;

    /// <summary>Render size of the plugin bitmap in the details header.</summary>
    private const int PluginDetailsIconSize = 48;

    private static OptionsPluginItem CreateOptionsPluginItem(
        PluginDocument descriptor,
        IReadOnlyDictionary<string, DiscoveredPlugin> discoveredById,
        IReadOnlyDictionary<string, PluginInstallState> installStates)
    {
        discoveredById.TryGetValue(descriptor.Id, out var discovered);

        var authorName = discovered?.Author?.Name ?? descriptor.Author?.Name;
        var version = discovered is null ? descriptor.Version : discovered.Version.ToString();
        var persistedIcon = PluginIconKey.TryDecodePersisted(descriptor.IconPng);
        return new OptionsPluginItem(
            discovered?.Name ?? descriptor.Name,
            string.IsNullOrWhiteSpace(authorName) ? LocalizationService.Current.Get(TextKeys.UnknownAuthor) : authorName,
            $"{descriptor.Id} v{version}",
            isEnabled: descriptor.IsEnabled,
            id: descriptor.Id,
            authorUrl: discovered?.Author?.Url ?? descriptor.Author?.Url,
            description: discovered?.Description ?? descriptor.Description,
            primaryExtension: discovered?.PrimaryExtension ?? descriptor.PrimaryExtension,
            projectUrl: discovered?.ProjectUrl ?? descriptor.ProjectUrl,
            version: version,
            capabilityType: ResolveCapabilityType(discovered),
            sizeBytes: PluginFolderSize.TryComputeBytes(discovered?.AssemblyLocation),
            updateSource: ResolveUpdateSource(descriptor.Id, discovered, installStates),
            icon: discovered?.GetIconNearest(PluginDetailsIconSize) ?? persistedIcon,
            listIcon: discovered?.GetIconNearest(PluginListIconSize) ?? persistedIcon);
    }

    private static string? ResolveCapabilityType(DiscoveredPlugin? plugin)
    {
        if (plugin is null || plugin.Capabilities.Count == 0)
        {
            return null;
        }

        return string.Join(", ", plugin.Capabilities
            .Select(static capability => capability.GetType().Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
    }

    private static PluginUpdateSource? ResolveUpdateSource(
        string pluginId,
        DiscoveredPlugin? discovered,
        IReadOnlyDictionary<string, PluginInstallState> installStates)
    {
        if (discovered is null)
        {
            return null;
        }

        return installStates.TryGetValue(pluginId, out var state) &&
               state.Status != PluginInstallOperationStatus.NotInstalled
            ? PluginUpdateSource.Marketplace
            : PluginUpdateSource.PluginFolder;
    }

    private async Task<bool> ShowResetStatusesPromptAsync()
    {
        var dialog = new ResetStatusesConfirmWindow();
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private async Task<bool> ShowResetScanRulesPromptAsync()
    {
        var dialog = new ResetScanRulesConfirmWindow();
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private async Task<bool> ShowResetMimeExclusionsPromptAsync()
    {
        var dialog = new ResetMimeExclusionsConfirmWindow();
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private static int ToAiScanMaxFileSizeMegabytes(long maxFileSizeBytes)
    {
        const long bytesPerMegabyte = 1024L * 1024L;
        var normalized = maxFileSizeBytes > 0 ? maxFileSizeBytes : AiScanPolicy.DefaultMaxFileSizeBytes;
        return (int)Math.Clamp(normalized / bytesPerMegabyte, 1, int.MaxValue);
    }
}
