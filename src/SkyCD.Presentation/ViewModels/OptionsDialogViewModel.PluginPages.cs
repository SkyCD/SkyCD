using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    private readonly Dictionary<string, OptionsPluginPage> pluginPages = new(StringComparer.OrdinalIgnoreCase);
    private PluginConfigurationManager? configurationManager;
    private bool disposed;

    public void SetPluginConfigurations(PluginConfigurationManager manager, TimeSpan timeout)
    {
        configurationManager = manager;
        var registrations = manager.GetRegistrations(includeDisabled: true);
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            var key = $"plugin:{registration.PluginId}/{registration.Descriptor.ConfigurationId}";
            available.Add(key);
            if (!pluginPages.ContainsKey(key)) pluginPages[key] = new OptionsPluginPage(registration, manager, timeout);
        }
        foreach (var key in pluginPages.Keys.Where(key => !available.Contains(key)).ToArray())
        {
            pluginPages[key].Dispose();
            pluginPages.Remove(key);
        }
        RefreshCategoriesPreservingSelection();
        OnPropertyChanged(nameof(SelectedPluginConfigurationPage));
        OnPropertyChanged(nameof(HasPluginConfigurationPage));
    }

    public void RestoreSettingCategory(string? categoryId, int legacyTabIndex)
    {
        SelectedSettingCategory = SettingCategories.FirstOrDefault(category => category.Id == categoryId)
            ?? SettingCategories.FirstOrDefault(category => category.BuiltInIndex == legacyTabIndex)
            ?? SettingCategories[0];
    }

    private void OnPluginEnabledChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptionsPluginItem.IsEnabled)) RefreshCategoriesPreservingSelection();
    }

    private void RefreshCategoriesPreservingSelection(string? selectedId = null)
    {
        selectedId ??= SelectedSettingCategory?.Id;
        OnPropertyChanged(nameof(SettingCategories));
        var categories = SettingCategories;
        var index = categories.ToList().FindIndex(category => category.Id == selectedId);
        // Never reinterpret a removed category's index as a different plugin.
        SelectedTabIndex = index >= 0 ? index : 0;
        SelectedSettingCategory = categories[SelectedTabIndex];
        if (selectedId is not null && index < 0) SettingsSearchText = string.Empty;
        RefreshFilteredCategories();
        NotifySettingsVisibilityChanged();
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<string, string?>>?> PreparePluginSettingsAsync()
    {
        var settings = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in SettingCategories.Where(category => category.PluginPage?.IsVisited == true).ToArray())
        {
            var page = category.PluginPage!;
            var preparation = await page.PrepareAsync();
            if (disposed || preparation is null) return null;
            switch (preparation.Status)
            {
                case PluginPageSaveStatus.Unavailable:
                    continue;
                case PluginPageSaveStatus.Rejected:
                    SettingsSearchText = string.Empty;
                    SelectedSettingCategory = category;
                    return null;
                default:
                    settings[page.Registration.PluginId] = preparation.Settings!;
                    break;
            }
        }
        return settings;
    }

    private bool SavePluginSettings(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> settings)
    {
        try
        {
            configurationManager?.SaveSettings(settings);
            return true;
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(GetType(), "configure", exception);
            var affected = SettingCategories.FirstOrDefault(category => category.PluginPage is { } page && settings.ContainsKey(page.Registration.PluginId));
            if (affected?.PluginPage is { } page)
            {
                page.Error = LocalizationService.Current.Format(TextKeys.PluginConfigurationSaveFailed, page.Registration.PluginName);
                SettingsSearchText = string.Empty;
                SelectedSettingCategory = affected;
            }
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var plugin in Plugins) plugin.PropertyChanged -= OnPluginEnabledChanged;
        foreach (var page in pluginPages.Values) page.Dispose();
        pluginPages.Clear();
        DisposeMarketplace();
    }
}
