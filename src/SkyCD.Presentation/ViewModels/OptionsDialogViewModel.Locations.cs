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

    [ObservableProperty] private string locationsStatusText = string.Empty;

    private readonly List<LocationChange> stagedLocationChanges = [];

    /// <summary>
    /// Raised when the merge toolbar action runs; the host answers by showing the destination picker and
    /// calling <see cref="ConfirmMergeLocations"/> when the user confirms it.
    /// </summary>
    public event EventHandler? MergeLocationsRequested;

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

        LocationsStatusText = string.Empty;
        SelectedLocation = Locations.FirstOrDefault();
        NotifyLocationCommands();
    }

    /// <summary>
    /// Returns the staged edits in the order they were made. An empty set means the dialog must not touch
    /// locations or catalog assignments.
    /// </summary>
    public LocationChangeSet BuildLocationChangeSet() => new(stagedLocationChanges);

    /// <summary>
    /// Appends an unstored row and opens its name editor. Nothing is staged until the row is named, so
    /// cancelling an unnamed row leaves the stored state untouched.
    /// </summary>
    [RelayCommand]
    private void CreateLocation()
    {
        var created = new LocationItemViewModel($"new:{Guid.NewGuid():N}", string.Empty, isStored: false);
        Locations.Add(created);
        SelectedLocation = created;
        LocationsStatusText = string.Empty;
        BeginLocationEdit(created);
        NotifyLocationCommands();
    }

    /// <summary>Opens the inline name editor on <paramref name="row"/>; only one row edits at a time.</summary>
    public void BeginLocationEdit(LocationItemViewModel row)
    {
        if (!Locations.Contains(row))
        {
            return;
        }

        foreach (var other in Locations.Where(location => location.IsEditing && !ReferenceEquals(location, row)).ToArray())
        {
            LeaveLocationEdit(other);
        }

        row.BeginEdit();
    }

    /// <summary>
    /// Commits the inline editor of <paramref name="row"/> using the same validation as the staged list:
    /// empty and duplicate names keep the editor open with the status message.
    /// </summary>
    /// <returns>True when the row left edit mode.</returns>
    public bool CommitLocationRename(LocationItemViewModel row)
    {
        if (!row.IsEditing)
        {
            return true;
        }

        var name = row.EditName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            LocationsStatusText = LocalizationService.Current.Get(TextKeys.LocationNameRequired);
            return false;
        }

        if (FindDuplicate(name) is { } duplicate && !ReferenceEquals(duplicate, row))
        {
            LocationsStatusText = new LocationDuplicateNameException(name).Message;
            return false;
        }

        if (string.Equals(row.Name, name, StringComparison.Ordinal))
        {
            row.CancelEdit();
            return true;
        }

        var isCreation = !row.IsStored && string.IsNullOrEmpty(row.PendingCreateName);
        if (row.IsStored)
        {
            // Only the current name matters: the store derives the id from it, so earlier renames of the
            // same row in this session need no replay.
            stagedLocationChanges.RemoveAll(change =>
                change is LocationChange.Rename rename &&
                string.Equals(rename.LocationId, row.LocationId, StringComparison.Ordinal));
            stagedLocationChanges.Add(new LocationChange.Rename(row.LocationId, row.Name, name));
        }
        else
        {
            // Unstored rows have no definition yet; the host creates them under the name shown on Save.
            SetPendingCreationName(row, name);
        }

        row.Name = name;
        row.CancelEdit();
        LocationsStatusText = isCreation
            ? LocalizationService.Current.Format(TextKeys.LocationCreated, name)
            : LocalizationService.Current.Format(TextKeys.LocationRenamed, name);
        return true;
    }

    /// <summary>
    /// Drops the draft and closes the editor. A row created in this session that was never named is
    /// removed again, because it stands for no pending creation.
    /// </summary>
    public void CancelLocationRename(LocationItemViewModel row)
    {
        if (row.IsEditing)
        {
            LeaveLocationEdit(row);
        }
    }

    /// <summary>
    /// Commits an open inline editor when the dialog is accepted. An invalid name keeps the dialog open
    /// and brings the Locations page forward so the status message is visible.
    /// </summary>
    public bool CommitPendingLocationRename()
    {
        foreach (var row in Locations.Where(location => location.IsEditing).ToArray())
        {
            if (!CommitLocationRename(row))
            {
                SettingsSearchText = string.Empty;
                SelectedTabIndex = LocationsCategoryIndex;
                return false;
            }
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanBeginRenameLocation))]
    private void BeginRenameLocation()
    {
        if (SelectedLocation is { } row)
        {
            BeginLocationEdit(row);
        }
    }

    private bool CanBeginRenameLocation() => SelectedLocation is not null;

    /// <summary>
    /// The host shows the destination picker for this command and stages the merge on confirm.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMergeLocations))]
    private void MergeLocations() => MergeLocationsRequested?.Invoke(this, EventArgs.Empty);

    private bool CanMergeLocations() =>
        SelectedLocation is { IsStored: true } source &&
        Locations.Any(location => location.IsStored && !ReferenceEquals(location, source));

    /// <summary>Rows the merge dialog may offer as a destination: stored rows other than the source.</summary>
    public IReadOnlyList<LocationItemViewModel> GetMergeDestinations(LocationItemViewModel source) =>
        Locations
            .Where(location => location.IsStored && !ReferenceEquals(location, source))
            .ToArray();

    /// <summary>Stages the merge chosen in the destination picker.</summary>
    public void ConfirmMergeLocations(LocationItemViewModel source, LocationItemViewModel target)
    {
        if (ReferenceEquals(source, target) ||
            !Locations.Contains(source) ||
            !Locations.Contains(target))
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
        LocationsStatusText = LocalizationService.Current.Format(TextKeys.LocationMerged, source.Name, target.Name);
        SelectedLocation = target;
        NotifyLocationCommands();
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
        NotifyLocationCommands();
    }

    private bool CanDeleteLocation() => SelectedLocation is not null;

    partial void OnSelectedLocationChanged(LocationItemViewModel? value) => NotifyLocationCommands();

    private void NotifyLocationCommands()
    {
        BeginRenameLocationCommand.NotifyCanExecuteChanged();
        DeleteLocationCommand.NotifyCanExecuteChanged();
        MergeLocationsCommand.NotifyCanExecuteChanged();
    }

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
        else if (!row.IsStored)
        {
            stagedLocationChanges.Add(new LocationChange.Create(name));
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
                row.PendingCreateName is not null &&
                string.Equals(created.Name, row.PendingCreateName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void LeaveLocationEdit(LocationItemViewModel row)
    {
        row.CancelEdit();

        // An unstored row that never received a name stands for no pending creation, so cancelling its
        // editor abandons the row altogether instead of leaving a blank entry behind.
        if (!row.IsStored && string.IsNullOrEmpty(row.PendingCreateName))
        {
            Locations.Remove(row);
            if (ReferenceEquals(SelectedLocation, row))
            {
                SelectedLocation = Locations.FirstOrDefault();
            }

            NotifyLocationCommands();
        }
    }
}
