using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.Plugins;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Browse view of the plugin catalog beside the installed plugins in the Options dialog. Installed and
/// catalog content are independent: a failed catalog load never hides installed plugins.
/// </summary>
public partial class OptionsDialogViewModel
{
    private IPluginMarketplaceService? marketplaceService;
    private CancellationTokenSource? marketplaceLifetime;
    private IReadOnlyList<MarketplacePluginItem> allMarketplaceItems = [];
    private bool marketplaceLoaded;
    private bool marketplaceCatalogIsEmpty;

    public ObservableCollection<MarketplacePluginItem> FilteredMarketplacePlugins { get; } = [];

    [ObservableProperty] private bool isMarketplaceBrowseSelected;

    [ObservableProperty] private bool isMarketplaceLoading;

    [ObservableProperty] private string marketplaceStatusMessage = string.Empty;

    [ObservableProperty] private string marketplaceFilterText = string.Empty;

    [ObservableProperty] private MarketplacePluginItem? selectedMarketplacePlugin;

    [ObservableProperty] private bool isInstallConfirmationVisible;

    public bool HasSelectedMarketplacePlugin => SelectedMarketplacePlugin is not null;

    /// <summary>The marketplace Overview tab shows its secondary "no description" line for a selected package without a summary.</summary>
    public bool ShowMarketplacePluginSummaryEmptyState =>
        SelectedMarketplacePlugin is { HasSummary: false };

    public bool HasSelectedMarketplaceProjectUrl =>
        ExternalLinkLauncher.IsLaunchable(SelectedMarketplacePlugin?.ProjectUrl);

    public string ConfirmationPrompt => SelectedMarketplacePlugin is { } item
        ? LocalizationService.Current.Format(TextKeys.PluginInstallConfirmation, item.Title, item.Publisher)
        : string.Empty;

    public bool ShowPluginViewSwitcher => IsPluginsCategorySelected && IsCurrentCategoryVisibleInSearch;

    public bool ShowInstalledPluginPathSection => ShowPluginPathSection && !IsMarketplaceBrowseSelected;

    public bool ShowInstalledPluginListSection => ShowPluginListSection && !IsMarketplaceBrowseSelected;

    public bool ShowInstalledPluginInfoSection => ShowPluginInfoSection && !IsMarketplaceBrowseSelected;

    public bool ShowMarketplaceSection =>
        IsPluginsCategorySelected && IsCurrentCategoryVisibleInSearch && IsMarketplaceBrowseSelected;

    public bool ShowMarketplaceEmptyState =>
        ShowMarketplaceSection && !IsMarketplaceLoading && FilteredMarketplacePlugins.Count == 0;

    public bool ShowMarketplaceStatusMessage => !string.IsNullOrWhiteSpace(MarketplaceStatusMessage);

    /// <summary>Attaches the catalog, installer and install-state services used by the Browse view.</summary>
    public void SetPluginMarketplace(IPluginMarketplaceService service)
    {
        marketplaceService = service ?? throw new ArgumentNullException(nameof(service));
        marketplaceLifetime ??= new CancellationTokenSource();
    }

    [RelayCommand]
    private void ShowInstalledPlugins()
    {
        IsMarketplaceBrowseSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedMarketplaceProjectUrl))]
    private void OpenSelectedMarketplaceProjectUrl()
    {
        ExternalLinkLauncher.TryOpen(SelectedMarketplacePlugin?.ProjectUrl);
    }

    private bool CanOpenSelectedMarketplaceProjectUrl() =>
        ExternalLinkLauncher.IsLaunchable(SelectedMarketplacePlugin?.ProjectUrl);

    [RelayCommand]
    private async Task ShowMarketplacePluginsAsync()
    {
        IsMarketplaceBrowseSelected = true;
        if (!marketplaceLoaded && !IsMarketplaceLoading)
        {
            await LoadMarketplaceAsync(forceRefresh: false);
        }
    }

    [RelayCommand]
    private async Task RefreshMarketplaceAsync()
    {
        if (marketplaceService is null)
        {
            return;
        }

        await LoadMarketplaceAsync(forceRefresh: true);
    }

    /// <summary>First click shows publisher, source and the code-execution warning; nothing is downloaded yet.</summary>
    [RelayCommand]
    private void RequestMarketplaceInstall(MarketplacePluginItem? item)
    {
        if (item is null || !item.CanInstallOrUpdate)
        {
            return;
        }

        SelectedMarketplacePlugin = item;
        IsInstallConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelMarketplaceInstall()
    {
        IsInstallConfirmationVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmMarketplaceInstallAsync()
    {
        if (marketplaceService is null || SelectedMarketplacePlugin is not { } item || !item.CanInstallOrUpdate)
        {
            IsInstallConfirmationVisible = false;
            return;
        }

        IsInstallConfirmationVisible = false;
        item.BeginInstall();
        try
        {
            var result = await marketplaceService.StageInstallAsync(
                item.Release!,
                marketplaceLifetime?.Token ?? CancellationToken.None);
            item.ApplyResult(result);
        }
        catch (OperationCanceledException) when (marketplaceLifetime?.IsCancellationRequested == true)
        {
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(GetType(), "install", exception);
            item.ApplyFailure(LocalizationService.Current.Format(TextKeys.PluginInstallFailedDetail, exception.Message));
        }

        OnPropertyChanged(nameof(HasAvailableMarketplaceUpdates));
        UpdateAllPluginsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Loads the catalog once, or force-refreshes it; a failure keeps the installed list untouched.</summary>
    public async Task LoadMarketplaceAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (marketplaceService is null || IsMarketplaceLoading)
        {
            return;
        }

        IsMarketplaceLoading = true;
        MarketplaceStatusMessage = string.Empty;
        marketplaceCatalogIsEmpty = false;
        try
        {
            var token = marketplaceLifetime is null
                ? cancellationToken
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, marketplaceLifetime.Token).Token;
            var snapshot = await marketplaceService.LoadAsync(forceRefresh, token);
            ApplyCatalogSnapshot(snapshot);
            marketplaceLoaded = true;
        }
        catch (OperationCanceledException) when (marketplaceLifetime?.IsCancellationRequested == true ||
                                                  cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(GetType(), "install", exception);
            MarketplaceStatusMessage = LocalizationService.Current.Get(TextKeys.PluginCatalogFailure);
        }
        finally
        {
            IsMarketplaceLoading = false;
            OnPropertyChanged(nameof(ShowMarketplaceEmptyState));
            OnPropertyChanged(nameof(ShowMarketplacePaneEmptyState));
        }
    }

    /// <summary>Loads the published snapshot once so the settings page can show repository states.</summary>
    private void EnsureMarketplaceSnapshot()
    {
        if (!ShowPluginsSettingsSection || marketplaceLoaded || IsMarketplaceLoading || marketplaceService is null)
        {
            return;
        }

        _ = LoadMarketplaceAsync(forceRefresh: false);
    }

    private void ApplyCatalogSnapshot(PluginCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ApplyPluginRepositoryStates(snapshot);
        MarketplaceStatusMessage = DescribeCatalog(snapshot);
        OnPropertyChanged(nameof(ShowMarketplaceStatusMessage));

        var states = marketplaceService?.GetInstallStates() ?? new Dictionary<string, PluginInstallState>();
        var items = snapshot.Packages
            .GroupBy(static package => package.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderBy(package => package,
                Comparer<MarketplacePackage>.Create(MarketplacePackage.CompareBestFirst)).First())
            .OrderBy(package => package, Comparer<MarketplacePackage>.Create(MarketplacePackage.CompareBestFirst))
            .Select(package => new MarketplacePluginItem(package, FindInstallState(states, package.PackageId)))
            .ToArray();

        allMarketplaceItems = items;
        marketplaceCatalogIsEmpty = items.Length == 0 &&
            !snapshot.IsOffline && !snapshot.HasTotalFailure && !snapshot.HasPartialFailure;
        RefreshFilteredMarketplacePlugins(keepSelection: true);
        OnPropertyChanged(nameof(HasAvailableMarketplaceUpdates));
        UpdateAllPluginsCommand.NotifyCanExecuteChanged();
    }

    private static PluginInstallState? FindInstallState(
        IReadOnlyDictionary<string, PluginInstallState> states, string packageId)
    {
        if (states.TryGetValue(packageId, out var direct) &&
            string.Equals(direct.PackageId ?? direct.PluginId, packageId, StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        return states.Values.FirstOrDefault(state =>
            string.Equals(state.PackageId ?? state.PluginId, packageId, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeCatalog(PluginCatalogSnapshot snapshot)
    {
        if (snapshot.IsOffline)
        {
            return LocalizationService.Current.Get(TextKeys.PluginCatalogOffline);
        }

        if (snapshot.HasTotalFailure)
        {
            return LocalizationService.Current.Get(TextKeys.PluginCatalogFailure);
        }

        if (snapshot.HasPartialFailure)
        {
            return LocalizationService.Current.Get(TextKeys.PluginCatalogPartialFailure);
        }

        return string.Empty;
    }

    private void RefreshFilteredMarketplacePlugins(bool keepSelection = false)
    {
        var previous = keepSelection ? SelectedMarketplacePlugin : null;
        var filtered = allMarketplaceItems
            .Where(item => item.MatchesFilter(MarketplaceFilterText))
            .ToArray();

        FilteredMarketplacePlugins.Clear();
        foreach (var item in filtered)
        {
            FilteredMarketplacePlugins.Add(item);
        }

        SelectedMarketplacePlugin = previous is not null && filtered.Contains(previous)
            ? previous
            : filtered.FirstOrDefault();
        OnPropertyChanged(nameof(ShowMarketplaceEmptyState));
        OnPropertyChanged(nameof(MarketplacePluginsHeader));
        OnPropertyChanged(nameof(MarketplacePluginsCount));
        OnPropertyChanged(nameof(ShowMarketplacePaneEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneFilterEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneList));
    }

    partial void OnMarketplaceFilterTextChanged(string value) => RefreshFilteredMarketplacePlugins();

    partial void OnSelectedMarketplacePluginChanged(MarketplacePluginItem? value)
    {
        IsInstallConfirmationVisible = false;
        OnPropertyChanged(nameof(HasSelectedMarketplacePlugin));
        OnPropertyChanged(nameof(ShowMarketplacePluginSummaryEmptyState));
        OnPropertyChanged(nameof(HasSelectedMarketplaceProjectUrl));
        OnPropertyChanged(nameof(ConfirmationPrompt));
        OpenSelectedMarketplaceProjectUrlCommand.NotifyCanExecuteChanged();
        if (value is not null) IsMarketplaceBrowseSelected = true;
    }

    partial void OnIsMarketplaceBrowseSelectedChanged(bool value) => NotifyMarketplaceVisibilityChanged();

    partial void OnIsMarketplaceLoadingChanged(bool value) => NotifyMarketplacePaneStateChanged();

    partial void OnMarketplaceStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(ShowMarketplaceStatusMessage));

    private void NotifyMarketplacePaneStateChanged()
    {
        OnPropertyChanged(nameof(ShowMarketplacePaneEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneFilterEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneList));
    }

    private void NotifyMarketplaceVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowPluginViewSwitcher));
        OnPropertyChanged(nameof(ShowInstalledPluginPathSection));
        OnPropertyChanged(nameof(ShowInstalledPluginListSection));
        OnPropertyChanged(nameof(ShowInstalledPluginInfoSection));
        OnPropertyChanged(nameof(ShowMarketplaceSection));
        OnPropertyChanged(nameof(ShowMarketplaceEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneFilterEmptyState));
        OnPropertyChanged(nameof(ShowMarketplacePaneList));
    }

    private void DisposeMarketplace()
    {
        try
        {
            marketplaceLifetime?.Cancel();
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(GetType(), "install", exception);
        }
        finally
        {
            marketplaceLifetime?.Dispose();
            marketplaceLifetime = null;
        }
    }
}
