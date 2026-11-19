using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Presentation.Locations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private string? locationFilter;

    /// <summary>
    /// Projects the resolved location name onto entries for display. Resolution runs against the full
    /// catalog so an entry shown alone still inherits the location assigned to an ancestor outside the
    /// visible subset (a media root, for example).
    /// </summary>
    private IReadOnlyList<CatalogDocument> ProjectLocations(IReadOnlyList<CatalogDocument> entries)
    {
        if (locationService is null)
        {
            return entries;
        }

        var all = GetAllCatalogEntries();
        locationService.ProjectLocations(all);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in all)
        {
            if (entry.HasLocation)
            {
                names[entry.Id] = entry.Location;
            }
        }

        foreach (var entry in entries)
        {
            entry.SetLocationName(names.TryGetValue(entry.Id, out var name) ? name : null);
        }

        return entries;
    }

    /// <summary>
    /// Location picker options for the add-to-list dialog, in display order.
    /// </summary>
    public IReadOnlyList<LocationItemViewModel> GetLocationChoices() =>
        locationService is null
            ? []
            : locationService.GetLocations()
                .Select(static location => new LocationItemViewModel(location.Id, location.Name))
                .ToArray();

    private bool MatchesLocationFilter(CatalogDocument entry) =>
        locationFilter is null
        || (locationService is not null &&
            string.Equals(locationService.ResolveEffectiveLocationId(entry), locationFilter, StringComparison.Ordinal));

    [RelayCommand]
    private void SetLocationFilter(string? locationId)
    {
        locationFilter = string.IsNullOrWhiteSpace(locationId) ? null : locationId;
        RefreshBrowserItemsForSelection();
        StatusText = locationFilter is null
            ? LocalizationService.Current.Get(TextKeys.LocationFilterCleared)
            : LocalizationService.Current.Format(
                TextKeys.LocationFilterApplied,
                locationService?.GetLocations().FirstOrDefault(location =>
                    string.Equals(location.Id, locationFilter, StringComparison.Ordinal))?.Name ?? locationFilter);
    }

    /// <summary>
    /// Builds the Locations context submenu: the manager dialog entry, an all-locations filter reset,
    /// and one filter entry per location with its effective usage count.
    /// </summary>
    private MainMenuItemViewModel BuildLocationsMenu()
    {
        var items = new List<MainMenuItemViewModel>
        {
            new() { Header = LocalizationService.Current.Get(UiKeys.ManageLocations), Command = OpenLocationsDialogCommand },
            new() { Header = LocalizationService.Current.Get(UiKeys.AllLocations), Command = SetLocationFilterCommand }
        };

        if (locationService is not null)
        {
            foreach (var location in locationService.GetLocations())
            {
                var count = locationService.CountUsages(location.Id);
                items.Add(new MainMenuItemViewModel
                {
                    Header = $"{location.Name} ({count})",
                    Command = SetLocationFilterCommand,
                    CommandParameter = location.Id
                });
            }
        }

        return new MainMenuItemViewModel
        {
            Key = "edit_locations",
            Header = LocalizationService.Current.Get(UiKeys.Locations),
            Items = items
        };
    }

    private void RefreshLocations()
    {
        RefreshTreeNodes(SelectedTreeNode?.Key);
        RefreshBrowserItemsForSelection();
        RefreshLocationMenuItems();
    }

    private void RefreshLocationMenuItems()
    {
        var parent = browserContextMenuItems.FirstOrDefault(item => string.Equals(item.Key, "edit_locations", StringComparison.Ordinal));
        if (parent is not null)
        {
            parent.Items = BuildLocationsMenu().Items;
        }

        parent = treeContextMenuItems.FirstOrDefault(item => string.Equals(item.Key, "edit_locations", StringComparison.Ordinal));
        if (parent is not null)
        {
            parent.Items = BuildLocationsMenu().Items;
        }
    }
}
