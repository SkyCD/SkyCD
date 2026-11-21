using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Presentation.Locations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Locations category of the Options dialog. Edits are staged on the list only: rows are created,
/// renamed, merged and deleted locally, and the ordered change set is applied by the host once the
/// dialog is accepted. Cancelling the dialog therefore leaves every location and catalog assignment
/// untouched. Browsing entries by location stays in the main window's Locations filter menus.
/// </summary>
public partial class OptionsDialogViewModel
{
    private const int LocationsCategoryIndex = 8;

    public ObservableCollection<LocationItemViewModel> Locations { get; } = [];

    [ObservableProperty] private LocationItemViewModel? selectedLocation;

    [ObservableProperty] private string newLocationName = string.Empty;

    [ObservableProperty] private string renameTo = string.Empty;

    [ObservableProperty] private LocationItemViewModel? mergeTarget;

    [ObservableProperty] private string locationsStatusText = string.Empty;

    private readonly List<LocationChange> stagedLocationChanges = [];

    public bool IsLocationsCategorySelected => SelectedBuiltInIndex == LocationsCategoryIndex;

    public bool ShowLocationsSection =>
        IsLocationsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    /// <summary>
    /// Loads the stored locations as the editable baseline. Rows created by the user get a temporary id
    /// and only exist in the dialog until the dialog is saved.
    /// </summary>
    public void SetLocations(IEnumerable<LocationSnapshot>? locations)
    {
        stagedLocationChanges.Clear();
        Locations.Clear();
        foreach (var location in locations ?? [])
        {
            if (string.IsNullOrWhiteSpace(location.Id) || string.IsNullOrWhiteSpace(location.Name))
            {
                continue;
            }

            Locations.Add(new LocationItemViewModel(location.Id, location.Name, location.UsageCount));
        }

        NewLocationName = string.Empty;
        RenameTo = string.Empty;
        MergeTarget = null;
        LocationsStatusText = string.Empty;
        SelectedLocation = Locations.FirstOrDefault();
        DeleteLocationCommand.NotifyCanExecuteChanged();
        MergeLocationsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Returns the staged edits in the order they were made. An empty set means the dialog must not touch
    /// locations or catalog assignments.
    /// </summary>
    public LocationChangeSet BuildLocationChangeSet() => new(stagedLocationChanges);

    [RelayCommand]
    private void CreateLocation()
    {
        var name = NewLocationName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            LocationsStatusText = LocalizationService.Current.Get(TextKeys.LocationNameRequired);
            return;
        }

        if (FindDuplicate(name) is not null)
        {
            LocationsStatusText = new LocationDuplicateNameException(name).Message;
            return;
        }

        // Unstored rows carry a temporary id and are only marked as created; the host creates them on Save.
        var created = new LocationItemViewModel($"new:{Guid.NewGuid():N}", name, isStored: false)
        {
            PendingCreateName = name
        };
        Locations.Add(created);
        stagedLocationChanges.Add(new LocationChange.Create(name));
        NewLocationName = string.Empty;
        LocationsStatusText = LocalizationService.Current.Format(TextKeys.LocationCreated, name);
        SelectedLocation = created;
    }

    [RelayCommand]
    private void RenameLocation()
    {
        if (SelectedLocation is not { } location)
        {
            return;
        }

        var name = RenameTo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            LocationsStatusText = LocalizationService.Current.Get(TextKeys.LocationNameRequired);
            return;
        }

        if (FindDuplicate(name) is { } duplicate && !ReferenceEquals(duplicate, location))
        {
            LocationsStatusText = new LocationDuplicateNameException(name).Message;
            return;
        }

        if (string.Equals(location.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        if (location.IsStored)
        {
            // Only the current name matters: the store derives the id from it, so earlier renames of the
            // same row in this session need no replay.
            stagedLocationChanges.RemoveAll(change =>
                change is LocationChange.Rename rename &&
                string.Equals(rename.LocationId, location.LocationId, StringComparison.Ordinal));
            stagedLocationChanges.Add(new LocationChange.Rename(location.LocationId, location.Name, name));
        }
        else
        {
            // Unstored rows have no definition yet; the host creates them under the name shown on Save.
            SetPendingCreationName(location, name);
        }

        location.Name = name;
        RenameTo = name;
        LocationsStatusText = LocalizationService.Current.Format(TextKeys.LocationRenamed, name);
    }

    [RelayCommand(CanExecute = nameof(CanMergeLocations))]
    private void MergeLocations()
    {
        if (SelectedLocation is not { } source || MergeTarget is not { } target || ReferenceEquals(source, target))
        {
            return;
        }

        // Staged rows have no stored definition yet, so there is no assignment history to merge.
        if (!source.IsStored || !target.IsStored)
        {
            LocationsStatusText = LocalizationService.Current.Get(UiKeys.LocationUnsaved);
            return;
        }

        stagedLocationChanges.Add(new LocationChange.Merge(source.LocationId, source.Name, target.LocationId));
        target.UsageCount += source.UsageCount;
        Locations.Remove(source);
        MergeTarget = null;
        LocationsStatusText = LocalizationService.Current.Format(TextKeys.LocationMerged, source.Name, target.Name);
        SelectedLocation = target;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteLocation))]
    private void DeleteLocation()
    {
        if (SelectedLocation is not { } location)
        {
            return;
        }

        // A row created in this session never reached the store: dropping it, and the pending creation it
        // stands for, keeps the stored state as it was.
        if (location.IsStored)
        {
            stagedLocationChanges.Add(new LocationChange.Delete(location.LocationId, location.Name));
        }
        else
        {
            RemovePendingCreation(location);
        }

        var usageCount = location.UsageCount;
        Locations.Remove(location);
        SelectedLocation = Locations.FirstOrDefault();
        LocationsStatusText = usageCount > 0
            ? LocalizationService.Current.Format(TextKeys.LocationDeletedInUse, location.Name, usageCount)
            : LocalizationService.Current.Format(TextKeys.LocationDeleted, location.Name);
    }

    private bool CanDeleteLocation() => SelectedLocation is not null;

    private bool CanMergeLocations() => SelectedLocation is not null && MergeTarget is not null;

    partial void OnSelectedLocationChanged(LocationItemViewModel? value)
    {
        if (value is not null)
        {
            RenameTo = value.Name;
        }

        DeleteLocationCommand.NotifyCanExecuteChanged();
        MergeLocationsCommand.NotifyCanExecuteChanged();
    }

    partial void OnMergeTargetChanged(LocationItemViewModel? value) => MergeLocationsCommand.NotifyCanExecuteChanged();

    private LocationItemViewModel? FindDuplicate(string name) =>
        Locations.FirstOrDefault(location =>
            string.Equals(location.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Keeps the pending creation of an unstored row in step with its row name, so the host creates the
    /// location under the name shown when the dialog is saved.
    /// </summary>
    private void SetPendingCreationName(LocationItemViewModel row, string name)
    {
        var index = FindPendingCreation(row);
        if (index >= 0)
        {
            stagedLocationChanges[index] = new LocationChange.Create(name);
        }

        row.PendingCreateName = name;
    }

    /// <summary>Removes the pending creation of an unstored row deleted before saving.</summary>
    private void RemovePendingCreation(LocationItemViewModel row)
    {
        var index = FindPendingCreation(row);
        if (index >= 0)
        {
            stagedLocationChanges.RemoveAt(index);
        }
    }

    private int FindPendingCreation(LocationItemViewModel row)
    {
        for (var index = 0; index < stagedLocationChanges.Count; index++)
        {
            if (stagedLocationChanges[index] is LocationChange.Create created &&
                string.Equals(created.Name, row.PendingCreateName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
