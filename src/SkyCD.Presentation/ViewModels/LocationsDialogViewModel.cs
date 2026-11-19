using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Locations;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.UndoRedo;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Location manager dialog: create, rename, merge, and delete physical storage
/// locations, and find entries carrying a location (including descendants that
/// inherit it) through the shared advanced-search contract. Definition changes
/// apply immediately and join the undo history; closing the dialog changes
/// nothing further.
/// </summary>
public sealed partial class LocationsDialogViewModel : ObservableObject
{
    private readonly CatalogLocationService? locationService;
    private readonly CatalogUndoManager? undoManager;
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly LocationDocumentRepository? locationRepository;
    private readonly Action? onChanged;
    private readonly Action<CatalogDocument>? navigate;

    public LocationsDialogViewModel(
        CatalogLocationService? locationService,
        CatalogUndoManager? undoManager,
        CatalogDocumentRepository? catalogRepository,
        LocationDocumentRepository? locationRepository,
        Action? onChanged = null,
        Action<CatalogDocument>? navigate = null)
    {
        this.locationService = locationService;
        this.undoManager = undoManager;
        this.catalogRepository = catalogRepository;
        this.locationRepository = locationRepository;
        this.onChanged = onChanged;
        this.navigate = navigate;
        RefreshLocations();
    }

    public ObservableCollection<LocationItemViewModel> Locations { get; } = [];

    public ObservableCollection<CatalogFindResult> FilterResults { get; } = [];

    [ObservableProperty]
    private LocationItemViewModel? selectedLocation;

    [ObservableProperty]
    private string newLocationName = string.Empty;

    [ObservableProperty]
    private string renameTo = string.Empty;

    [ObservableProperty]
    private LocationItemViewModel? mergeTarget;

    [ObservableProperty]
    private CatalogFindResult? selectedResult;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool dialogAccepted;

    public bool LocationsAvailable => locationService is not null;

    partial void OnSelectedLocationChanged(LocationItemViewModel? value)
    {
        if (value is not null)
        {
            RenameTo = value.Name;
        }

        FilterResults.Clear();
        ShowItemsCommand.NotifyCanExecuteChanged();
    }

    public void RefreshLocations()
    {
        var selectedId = SelectedLocation?.LocationId;
        Locations.Clear();
        if (locationService is null)
        {
            return;
        }

        foreach (var location in locationService.GetLocations())
        {
            Locations.Add(new LocationItemViewModel(location.Id, location.Name, locationService.CountUsages(location.Id)));
        }

        SelectedLocation = Locations.FirstOrDefault(item => string.Equals(item.LocationId, selectedId, StringComparison.Ordinal))
            ?? Locations.FirstOrDefault();
    }

    private bool CanManageLocations() => locationService is not null && locationRepository is not null;

    [RelayCommand(CanExecute = nameof(CanManageLocations))]
    private void CreateLocation()
    {
        if (locationService is null || locationRepository is null || catalogRepository is null)
        {
            return;
        }

        var name = NewLocationName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.LocationNameRequired);
            return;
        }

        try
        {
            var location = locationService.CreateLocation(name);
            undoManager?.RecordLocationCreated(locationRepository, catalogRepository, location.Id, location.Name);
            NewLocationName = string.Empty;
            StatusText = LocalizationService.Current.Format(TextKeys.LocationCreated, location.Name);
            RefreshLocations();
            SelectLocation(location.Id);
            onChanged?.Invoke();
        }
        catch (Exception exception) when (exception is LocationDuplicateNameException || exception is LocationNameRequiredException)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageLocations))]
    private void RenameLocation()
    {
        if (locationService is null || locationRepository is null || SelectedLocation is null)
        {
            return;
        }

        var name = RenameTo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.LocationNameRequired);
            return;
        }

        var oldName = SelectedLocation.Name;
        if (string.Equals(oldName, name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            locationService.RenameLocation(SelectedLocation.LocationId, name);
            undoManager?.RecordLocationRenamed(locationRepository, SelectedLocation.LocationId, oldName, name);
            SelectedLocation.Name = name;
            StatusText = LocalizationService.Current.Format(TextKeys.LocationRenamed, name);
            RefreshLocations();
            SelectLocation(SelectedLocation.LocationId);
            onChanged?.Invoke();
        }
        catch (Exception exception) when (exception is LocationDuplicateNameException
            || exception is LocationNameRequiredException
            || exception is LocationNotFoundException)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageLocations))]
    private void DeleteLocation()
    {
        if (locationService is null || locationRepository is null || catalogRepository is null || SelectedLocation is null)
        {
            return;
        }

        var locationId = SelectedLocation.LocationId;
        var locationName = SelectedLocation.Name;
        var usageCount = locationService.CountUsages(locationId);
        var affected = CollectAffected(locationService, locationId);
        try
        {
            locationService.DeleteLocation(locationId);
            undoManager?.RecordLocationDeleted(catalogRepository, locationRepository, locationId, locationName, affected);
            StatusText = usageCount > 0
                ? LocalizationService.Current.Format(TextKeys.LocationDeletedInUse, locationName, usageCount)
                : LocalizationService.Current.Format(TextKeys.LocationDeleted, locationName);
            RefreshLocations();
            onChanged?.Invoke();
        }
        catch (LocationNotFoundException exception)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageLocations))]
    private void MergeLocations()
    {
        if (locationService is null || locationRepository is null || catalogRepository is null || SelectedLocation is null)
        {
            return;
        }

        var source = SelectedLocation;
        var target = MergeTarget;
        if (target is null || string.Equals(source.LocationId, target.LocationId, StringComparison.Ordinal))
        {
            return;
        }

        var affected = CollectAffected(locationService, source.LocationId);
        try
        {
            locationService.MergeLocations(source.LocationId, target.LocationId);
            undoManager?.RecordLocationsMerged(catalogRepository, locationRepository, source.LocationId, source.Name, target.LocationId, affected);
            StatusText = LocalizationService.Current.Format(TextKeys.LocationMerged, source.Name, target.Name);
            MergeTarget = null;
            RefreshLocations();
            SelectLocation(target.LocationId);
            onChanged?.Invoke();
        }
        catch (LocationNotFoundException exception)
        {
            StatusText = exception.Message;
        }
    }

    /// <summary>
    /// Snapshots the own assignment of every entry directly carrying the location, so undo can restore
    /// exactly the stored state the operation rewrote. Inheriting descendants need no snapshot: they
    /// follow their ancestors again once the assignment is restored.
    /// </summary>
    private static Dictionary<string, string?> CollectAffected(
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

    private void SelectLocation(string locationId)
    {
        SelectedLocation = Locations.FirstOrDefault(item => string.Equals(item.LocationId, locationId, StringComparison.Ordinal));
    }

    private bool CanShowItems() => locationService is not null && SelectedLocation is not null;

    [RelayCommand(CanExecute = nameof(CanShowItems))]
    private void ShowItems()
    {
        FilterResults.Clear();
        if (locationService is null || catalogRepository is null || SelectedLocation is null)
        {
            return;
        }

        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Location,
                        Operator = AdvancedCatalogOperator.Equals,
                        Value = SelectedLocation.Name
                    }
                ]
            },
            Take = AdvancedCatalogSearchService.MaxTake
        };

        var index = new CatalogSearchIndex();
        index.Rebuild(locationService.ProjectLocations(catalogRepository.GetAll()));
        var matches = AdvancedCatalogSearchService.Search(index, query);
        foreach (var match in matches)
        {
            FilterResults.Add(match);
        }

        StatusText = LocalizationService.Current.Format(TextKeys.LocationFilterMatchCount, matches.Count, SelectedLocation.Name);
    }

    private bool CanNavigate() => navigate is not null && SelectedResult is not null;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void NavigateToSelected()
    {
        if (navigate is not null && SelectedResult is not null)
        {
            navigate(SelectedResult.Document);
        }
    }

    partial void OnSelectedResultChanged(CatalogFindResult? value) => NavigateToSelectedCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }
}
