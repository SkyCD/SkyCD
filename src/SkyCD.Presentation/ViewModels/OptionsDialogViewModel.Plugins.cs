using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Marketplace;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>Which section of the Options plug-ins page is visible.</summary>
public enum PluginSectionFilter
{
    All,
    Installed,
    Marketplace
}

/// <summary>
/// JetBrains-style plug-ins page: a shared search filter, an installed list with a collapsible
/// marketplace below it, an "Update All" action and a per-plugin detail pane with tabs.
/// </summary>
public partial class OptionsDialogViewModel
{
    [ObservableProperty] private string pluginSearchText = string.Empty;

    [ObservableProperty] private PluginSectionFilter pluginSectionFilter;

    [ObservableProperty] private bool isMarketplaceSectionExpanded;

    [ObservableProperty] private bool isPluginConfigurationTabSelected;

    public bool ShowInstalledPluginSection => PluginSectionFilter != PluginSectionFilter.Marketplace;

    public bool ShowMarketplacePluginSection => PluginSectionFilter != PluginSectionFilter.Installed;

    public string InstalledPluginsHeader =>
        LocalizationService.Current.Format(UiKeys.PluginInstalledSectionHeader, FilteredPlugins.Count);

    public string MarketplacePluginsHeader =>
        LocalizationService.Current.Format(UiKeys.PluginMarketplaceSectionHeader, FilteredMarketplacePlugins.Count);

    public int InstalledPluginsCount => FilteredPlugins.Count;

    public int MarketplacePluginsCount => FilteredMarketplacePlugins.Count;

    public string MarketplaceSectionToggleText =>
        LocalizationService.Current.Get(IsMarketplaceSectionExpanded ? UiKeys.PluginShowLess : UiKeys.PluginShowMore);

    public bool ShowMarketplaceSectionContent => IsMarketplaceSectionExpanded;

    public bool ShowMarketplacePaneEmptyState =>
        IsMarketplaceSectionExpanded && !IsMarketplaceLoading && FilteredMarketplacePlugins.Count == 0;

    public bool HasAvailableMarketplaceUpdates =>
        allMarketplaceItems.Any(static item => item.UpdateAvailable);

    public OptionsPluginPage? SelectedPluginConfigurationPage => SelectedPlugin is { } plugin
        ? pluginPages.Values.FirstOrDefault(page =>
            string.Equals(page.Registration.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase))
        : null;

    public bool HasPluginConfigurationPage => SelectedPluginConfigurationPage is not null;

    public string SelectedPluginDetailsVersion => SelectedPlugin?.ExtendedInfo ?? string.Empty;

    public string SelectedPluginDetailsId => SelectedPlugin?.Id ?? string.Empty;

    public string SelectedPluginDetailsType => SelectedPlugin?.Type ?? string.Empty;

    public string SelectedPluginDetailsExtension => SelectedPlugin?.PrimaryExtension ?? string.Empty;

    public bool HasSelectedPluginDetailsExtension => SelectedPlugin?.PrimaryExtension is not null;

    [RelayCommand]
    private void ShowAllPluginSections() => PluginSectionFilter = PluginSectionFilter.All;

    [RelayCommand]
    private void ShowInstalledPluginsOnly() => PluginSectionFilter = PluginSectionFilter.Installed;

    [RelayCommand]
    private void ShowMarketplacePluginsOnly() => PluginSectionFilter = PluginSectionFilter.Marketplace;

    [RelayCommand]
    private async Task ToggleMarketplaceSectionAsync()
    {
        IsMarketplaceSectionExpanded = !IsMarketplaceSectionExpanded;
        if (IsMarketplaceSectionExpanded && !marketplaceLoaded && !IsMarketplaceLoading)
        {
            await LoadMarketplaceAsync(forceRefresh: false);
        }
    }

    /// <summary>Rescans installed plug-ins, then force-refreshes the catalog so the update set is fresh.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAllPlugins))]
    private async Task UpdateAllPluginsAsync()
    {
        RefreshPlugins();
        if (marketplaceService is null)
        {
            return;
        }

        await LoadMarketplaceAsync(forceRefresh: true);
    }

    private bool CanUpdateAllPlugins() => HasAvailableMarketplaceUpdates;

    partial void OnPluginSearchTextChanged(string value)
    {
        PluginFilterText = value;
        MarketplaceFilterText = value;
        NotifyPluginsPageChanged();
        IsPluginConfigurationTabSelected = false;
    }

    partial void OnPluginSectionFilterChanged(PluginSectionFilter value)
    {
        OnPropertyChanged(nameof(ShowInstalledPluginSection));
        OnPropertyChanged(nameof(ShowMarketplacePluginSection));
    }

    partial void OnIsMarketplaceSectionExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(MarketplaceSectionToggleText));
        OnPropertyChanged(nameof(ShowMarketplaceSectionContent));
        OnPropertyChanged(nameof(ShowMarketplacePaneEmptyState));
    }

    partial void OnIsPluginConfigurationTabSelectedChanged(bool value)
    {
        if (value && SelectedPluginConfigurationPage is { } page)
        {
            _ = page.LoadAsync();
        }
    }

    private void NotifyPluginsPageChanged()
    {
        OnPropertyChanged(nameof(InstalledPluginsHeader));
        OnPropertyChanged(nameof(MarketplacePluginsHeader));
        OnPropertyChanged(nameof(InstalledPluginsCount));
        OnPropertyChanged(nameof(MarketplacePluginsCount));
        OnPropertyChanged(nameof(ShowMarketplacePaneEmptyState));
        UpdateAllPluginsCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedPluginDetailsChanged()
    {
        OnPropertyChanged(nameof(SelectedPluginConfigurationPage));
        OnPropertyChanged(nameof(HasPluginConfigurationPage));
        OnPropertyChanged(nameof(SelectedPluginDetailsVersion));
        OnPropertyChanged(nameof(SelectedPluginDetailsId));
        OnPropertyChanged(nameof(SelectedPluginDetailsType));
        OnPropertyChanged(nameof(SelectedPluginDetailsExtension));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsExtension));
    }
}
