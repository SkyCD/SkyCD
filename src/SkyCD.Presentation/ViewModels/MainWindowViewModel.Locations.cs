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
    /// Builds the Locations context submenu: the manager entry, an all-locations filter reset,
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

    /// <summary>
    /// Returns the stored locations with their effective usage counts, as the Options dialog needs them
    /// when it opens its Locations category.
    /// </summary>
    public IReadOnlyList<LocationSnapshot> GetLocationSnapshots()
    {
        if (locationService is null)
        {
            return [];
        }

        return locationService.GetLocations()
            .Select(location => new LocationSnapshot(location.Id, location.Name, locationService.CountUsages(location.Id)))
            .ToArray();
    }

    /// <summary>
    /// Applies the location edits staged in the Options dialog, in the order the user made them. Each
    /// definition change joins the undo history exactly as a direct edit would, and the visible location
    /// names, tree, browser and location menus are refreshed once at the end. An empty change set changes
    /// nothing, so cancelling the dialog can never touch stored locations or catalog assignments.
    /// </summary>
    public void ApplyLocationChanges(LocationChangeSet? changes)
    {
        if (changes is null || changes.IsEmpty ||
            locationService is null || locationRepository is null || catalogRepository is null)
        {
            return;
        }

        foreach (var change in changes.Changes)
        {
            ApplyLocationChange(change);
        }

        IsDirtyDocument = true;
        UpdateUndoRedoState();
        RefreshLocations();
    }

    private void ApplyLocationChange(LocationChange change)
    {
        var service = locationService!;
        try
        {
            switch (change)
            {
                case LocationChange.Create create:
                    var created = service.CreateLocation(create.Name);
                    undoManager?.RecordLocationCreated(locationRepository!, catalogRepository!, created.Id, created.Name);
                    break;

                case LocationChange.Rename rename:
                    service.RenameLocation(rename.LocationId, rename.NewName);
                    undoManager?.RecordLocationRenamed(locationRepository!, rename.LocationId, rename.OldName, rename.NewName);
                    break;

                case LocationChange.Merge merge:
                    ApplyLocationMerge(service, merge);
                    break;

                case LocationChange.Delete delete:
                    var affected = CollectLocationAffected(service, delete.LocationId);
                    service.DeleteLocation(delete.LocationId);
                    undoManager?.RecordLocationDeleted(
                        catalogRepository!, locationRepository!, delete.LocationId, delete.Name, affected);
                    break;
            }
        }
        catch (LocationNotFoundException exception)
        {
            // A staged edit whose location disappeared meanwhile; the stored state already matches the intent.
            StatusText = exception.Message;
        }
        catch (LocationDuplicateNameException exception)
        {
            StatusText = exception.Message;
        }
    }

    private void ApplyLocationMerge(CatalogLocationService service, LocationChange.Merge merge)
    {
        var source = service.GetLocations()
            .FirstOrDefault(location => string.Equals(location.Id, merge.SourceId, StringComparison.Ordinal));
        var targetId = merge.TargetId;
        var targetExists = service.GetLocations()
            .Any(location => string.Equals(location.Id, targetId, StringComparison.Ordinal));
        if (source is null || !targetExists)
        {
            return;
        }

        var affected = CollectLocationAffected(service, merge.SourceId);
        service.MergeLocations(merge.SourceId, targetId);
        undoManager?.RecordLocationsMerged(
            catalogRepository!, locationRepository!, merge.SourceId, merge.SourceName, targetId, affected);
    }

    /// <summary>
    /// Snapshots the own assignment of every entry directly carrying the location, so undo can restore
    /// exactly the stored state the operation rewrote. Inheriting descendants need no snapshot: they
    /// follow their ancestors again once the assignment is restored.
    /// </summary>
    private static Dictionary<string, string?> CollectLocationAffected(
        CatalogLocationService service, string locationId)
    {
        var affected = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in service.GetEntriesWithLocation(locationId))
        {
            var own = service.GetEntryOwnLocationId(entry.Id);
            if (string.Equals(own, locationId, StringComparison.Ordinal))
            {
                affected[entry.Id] = own;
            }
        }

        return affected;
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
