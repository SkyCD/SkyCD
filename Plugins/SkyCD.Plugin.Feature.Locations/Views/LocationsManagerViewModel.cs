using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.Plugin.Feature.Locations.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Locations.Views;

/// <summary>
/// Location manager page. Stored actions go straight through the host location API, so the catalog is
/// updated immediately; a newly added row is the one exception and stays unstored until its inline
/// editor commits.
/// </summary>
public sealed partial class LocationsManagerViewModel : ObservableObject
{
    private readonly ILocationApi api;
    private readonly Action? onChanged;

    public LocationsManagerViewModel(ILocationApi api, Action? onChanged = null)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.onChanged = onChanged;
        Reload();
    }

    public ObservableCollection<LocationRow> Locations { get; } = [];

    [ObservableProperty] private LocationRow? selectedLocation;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string errorText = string.Empty;
    [ObservableProperty] private bool hasLocations;
    [ObservableProperty] private bool isCatalogOpen;

    public bool ShowCatalogUnavailable => !IsCatalogOpen;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>Raised when the merge action runs; the view answers by showing the destination picker.</summary>
    public event EventHandler? MergeLocationsRequested;

    partial void OnIsCatalogOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCatalogUnavailable));
        NotifyCommands();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedLocationChanged(LocationRow? value) => NotifyCommands();

    [RelayCommand(CanExecute = nameof(CanAddLocation))]
    private void AddLocation()
    {
        ErrorText = string.Empty;
        var draft = new LocationRow($"new:{Guid.NewGuid():N}", string.Empty, 0, isStored: false);
        Locations.Add(draft);
        SelectedLocation = draft;
        BeginLocationEdit(draft);
        NotifyCommands();
    }

    private bool CanAddLocation() => IsCatalogOpen;

    [RelayCommand(CanExecute = nameof(CanDeleteLocation))]
    private void DeleteLocation()
    {
        ErrorText = string.Empty;
        if (SelectedLocation is not { } selected)
        {
            return;
        }

        if (!selected.IsStored)
        {
            CancelLocationRename(selected);
            return;
        }

        var usageCount = selected.UsageCount;
        try
        {
            api.DeleteLocation(selected.Id);
            Reload();
            StatusText = usageCount > 0
                ? LocalizationService.Current.Format(LocationsKeys.LocationDeletedInUse, selected.Name, usageCount)
                : LocalizationService.Current.Format(LocationsKeys.LocationDeleted, selected.Name);
            onChanged?.Invoke();
        }
        catch (LocationApiException exception)
        {
            ErrorText = Describe(exception);
        }
    }

    private bool CanDeleteLocation() => SelectedLocation is not null;

    [RelayCommand(CanExecute = nameof(CanMergeLocations))]
    private void MergeLocations() => MergeLocationsRequested?.Invoke(this, EventArgs.Empty);

    private bool CanMergeLocations() =>
        SelectedLocation is { IsStored: true } source &&
        Locations.Any(location => location.IsStored && !ReferenceEquals(location, source));

    /// <summary>Opens the inline name editor on <paramref name="row"/>; only one row edits at a time.</summary>
    public void BeginLocationEdit(LocationRow row)
    {
        if (!Locations.Contains(row))
        {
            return;
        }

        foreach (var other in Locations.Where(location => location.IsEditing && !ReferenceEquals(location, row)).ToArray())
        {
            CancelLocationRename(other);
        }

        row.BeginEdit();
        NotifyCommands();
    }

    /// <summary>
    /// Commits the inline editor using the host API. Empty names and API validation failures keep the
    /// editor open with the error text.
    /// </summary>
    public bool CommitLocationRename(LocationRow row)
    {
        if (!row.IsEditing)
        {
            return true;
        }

        var name = row.EditName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText = LocalizationService.Current.Get(LocationsKeys.LocationNameRequired);
            return false;
        }

        if (string.Equals(row.Name, name, StringComparison.Ordinal))
        {
            row.CancelEdit();
            return true;
        }

        try
        {
            var selectionId = SelectedLocation?.Id;
            if (row.IsStored)
            {
                api.RenameLocation(row.Id, name);
                Reload(selectionId ?? row.Id);
                StatusText = LocalizationService.Current.Format(LocationsKeys.LocationRenamed, name);
            }
            else
            {
                var created = api.CreateLocation(name);
                // Keep the row the user moved to, unless the draft itself was still selected.
                Reload(string.Equals(selectionId, row.Id, StringComparison.Ordinal) || selectionId is null
                    ? created.Id
                    : selectionId);
                StatusText = LocalizationService.Current.Format(LocationsKeys.LocationCreated, created.Name);
            }

            onChanged?.Invoke();
            return true;
        }
        catch (LocationApiException exception)
        {
            ErrorText = Describe(exception);
            return false;
        }
    }

    /// <summary>Drops the draft. An unstored row that was never named is removed again.</summary>
    public void CancelLocationRename(LocationRow row)
    {
        if (!row.IsStored)
        {
            Locations.Remove(row);
            if (ReferenceEquals(SelectedLocation, row))
            {
                SelectedLocation = Locations.FirstOrDefault();
            }
        }
        else
        {
            row.CancelEdit();
        }

        NotifyCommands();
    }

    /// <summary>Rows the merge dialog may offer as a destination: stored rows other than the source.</summary>
    public IReadOnlyList<LocationRow> GetMergeDestinations(LocationRow source) =>
        Locations.Where(location => location.IsStored && !ReferenceEquals(location, source)).ToArray();

    /// <summary>Merges the source into the destination chosen in the merge dialog.</summary>
    public void ConfirmMergeLocations(LocationRow source, LocationRow target)
    {
        ErrorText = string.Empty;
        if (ReferenceEquals(source, target) ||
            !Locations.Contains(source) ||
            !Locations.Contains(target))
        {
            return;
        }

        try
        {
            api.MergeLocations(source.Id, target.Id);
            Reload(target.Id);
            StatusText = LocalizationService.Current.Format(LocationsKeys.LocationMerged, source.Name, target.Name);
            onChanged?.Invoke();
        }
        catch (LocationApiException exception)
        {
            ErrorText = Describe(exception);
        }
    }

    private void Reload(string? selectId = null)
    {
        IsCatalogOpen = api.IsCatalogOpen;
        selectId ??= SelectedLocation?.Id;
        Locations.Clear();
        if (IsCatalogOpen)
        {
            foreach (var location in api.GetLocations())
            {
                Locations.Add(new LocationRow(location.Id, location.Name, location.UsageCount));
            }
        }

        HasLocations = Locations.Count > 0;
        SelectedLocation = Locations.FirstOrDefault(location => location.Id == selectId) ?? Locations.FirstOrDefault();
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        AddLocationCommand.NotifyCanExecuteChanged();
        DeleteLocationCommand.NotifyCanExecuteChanged();
        MergeLocationsCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(LocationApiException exception) => exception switch
    {
        LocationApiNameRequiredException => LocalizationService.Current.Get(LocationsKeys.LocationNameRequired),
        LocationApiDuplicateNameException duplicate =>
            LocalizationService.Current.Format(LocationsKeys.LocationNameDuplicate, duplicate.Name),
        LocationApiCatalogUnavailableException => LocalizationService.Current.Get(LocationsKeys.CatalogUnavailable),
        LocationApiNotFoundException or LocationApiEntryNotFoundException =>
            LocalizationService.Current.Get(LocationsKeys.LocationNotFound),
        _ => exception.Message
    };
}
