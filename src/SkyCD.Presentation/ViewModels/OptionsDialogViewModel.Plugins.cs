using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Formatting;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.Plugins;
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

    public bool ShowInstalledPluginsEmptyState => FilteredPlugins.Count == 0;

    public string MarketplaceSectionToggleText =>
        LocalizationService.Current.Get(IsMarketplaceSectionExpanded ? UiKeys.PluginShowLess : UiKeys.PluginShowMore);

    public bool ShowMarketplaceSectionContent => IsMarketplaceSectionExpanded;

    public bool ShowMarketplacePaneEmptyState =>
        IsMarketplaceSectionExpanded && !IsMarketplaceLoading && marketplaceCatalogIsEmpty;

    public bool ShowMarketplacePaneFilterEmptyState =>
        IsMarketplaceSectionExpanded && !IsMarketplaceLoading &&
        allMarketplaceItems.Count > 0 && FilteredMarketplacePlugins.Count == 0;

    public bool ShowMarketplacePaneList =>
        IsMarketplaceSectionExpanded && FilteredMarketplacePlugins.Count > 0;

    public bool HasAvailableMarketplaceUpdates =>
        allMarketplaceItems.Any(static item => item.UpdateAvailable);

    public OptionsPluginPage? SelectedPluginConfigurationPage => SelectedPlugin is { } plugin
        ? pluginPages.Values.FirstOrDefault(page =>
            string.Equals(page.Registration.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase))
        : null;

    public bool HasPluginConfigurationPage => SelectedPluginConfigurationPage is not null;

    public string SelectedPluginDetailsVersion => SelectedPlugin?.ExtendedInfo ?? string.Empty;

    public string SelectedPluginDetailsId => SelectedPlugin?.Id ?? string.Empty;

    /// <summary>Author/vendor row; the unknown-author fallback is applied when the item is built.</summary>
    public string SelectedPluginDetailsType => SelectedPlugin?.Type ?? string.Empty;

    public bool HasSelectedPluginDetailsType => !string.IsNullOrWhiteSpace(SelectedPluginDetailsType);

    public string SelectedPluginDetailsVersionNumber => SelectedPlugin?.Version ?? string.Empty;

    public bool HasSelectedPluginDetailsVersionNumber => !string.IsNullOrWhiteSpace(SelectedPluginDetailsVersionNumber);

    public string SelectedPluginDetailsSize => SelectedPlugin?.SizeBytes is { } bytes
        ? SizeFormatting.FormatBytes(bytes)
        : string.Empty;

    public bool HasSelectedPluginDetailsSize => SelectedPlugin?.SizeBytes is not null;

    public string SelectedPluginDetailsCapabilityType => SelectedPlugin?.CapabilityType ?? string.Empty;

    public bool HasSelectedPluginDetailsCapabilityType =>
        !string.IsNullOrWhiteSpace(SelectedPluginDetailsCapabilityType);

    public string SelectedPluginDetailsUpdateSource => SelectedPlugin?.UpdateSource switch
    {
        PluginUpdateSource.Marketplace => LocalizationService.Current.Get(UiKeys.PluginUpdatesFromMarketplace),
        PluginUpdateSource.PluginFolder => LocalizationService.Current.Get(UiKeys.PluginUpdatesFromPluginFolder),
        _ => string.Empty
    };

    public bool HasSelectedPluginDetailsUpdateSource => SelectedPlugin?.UpdateSource is not null;

    public bool HasSelectedPluginDetailsProjectUrl =>
        ExternalLinkLauncher.IsLaunchable(SelectedPlugin?.ProjectUrl);

    public bool HasSelectedPluginDetailsAuthorUrl =>
        ExternalLinkLauncher.IsLaunchable(SelectedPlugin?.AuthorUrl);

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
        OnPropertyChanged(nameof(ShowMarketplacePaneFilterEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneList));
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
        OnPropertyChanged(nameof(ShowMarketplacePaneFilterEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneList));
        UpdateAllPluginsCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedPluginDetailsChanged()
    {
        OnPropertyChanged(nameof(SelectedPluginConfigurationPage));
        OnPropertyChanged(nameof(HasPluginConfigurationPage));
        OnPropertyChanged(nameof(SelectedPluginDetailsVersion));
        OnPropertyChanged(nameof(SelectedPluginDetailsId));
        OnPropertyChanged(nameof(SelectedPluginDetailsType));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsType));
        OnPropertyChanged(nameof(SelectedPluginDetailsVersionNumber));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsVersionNumber));
        OnPropertyChanged(nameof(SelectedPluginDetailsSize));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsSize));
        OnPropertyChanged(nameof(SelectedPluginDetailsCapabilityType));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsCapabilityType));
        OnPropertyChanged(nameof(SelectedPluginDetailsUpdateSource));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsUpdateSource));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsProjectUrl));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsAuthorUrl));
        OnPropertyChanged(nameof(SelectedPluginDetailsExtension));
        OnPropertyChanged(nameof(HasSelectedPluginDetailsExtension));
    }
}
