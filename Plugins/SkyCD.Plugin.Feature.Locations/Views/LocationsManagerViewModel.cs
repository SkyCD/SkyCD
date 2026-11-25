using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.Plugin.Feature.Locations.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Locations.Views;

/// <summary>
/// Location manager dialog. Every action goes straight through the host location API, so the catalog is
/// updated immediately and cancelling or closing the dialog never rolls anything back.
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

    public ObservableCollection<LocationRow> MergeTargets { get; } = [];

    [ObservableProperty] private LocationRow? selectedLocation;
    [ObservableProperty] private LocationRow? mergeTarget;
    [ObservableProperty] private string newLocationName = string.Empty;
    [ObservableProperty] private string locationName = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string errorText = string.Empty;
    [ObservableProperty] private bool hasLocations;
    [ObservableProperty] private bool isCatalogOpen;

    public bool ShowCatalogUnavailable => !IsCatalogOpen;

    partial void OnIsCatalogOpenChanged(bool value) => OnPropertyChanged(nameof(ShowCatalogUnavailable));

    partial void OnSelectedLocationChanged(LocationRow? value)
    {
        LocationName = value?.Name ?? string.Empty;
        RebuildMergeTargets();
        NotifyCommands();
    }

    partial void OnMergeTargetChanged(LocationRow? value) => NotifyCommands();

    [RelayCommand]
    private void CreateLocation()
    {
        ErrorText = string.Empty;
        try
        {
            var created = api.CreateLocation(NewLocationName);
            NewLocationName = string.Empty;
            Reload(created.Id);
            StatusText = LocalizationService.Current.Format(LocationsKeys.LocationCreated, created.Name);
            onChanged?.Invoke();
        }
        catch (LocationApiException exception)
        {
            ErrorText = Describe(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void RenameLocation()
    {
        ErrorText = string.Empty;
        if (SelectedLocation is not { } selected)
        {
            return;
        }

        var name = LocationName.Trim();
        try
        {
            api.RenameLocation(selected.Id, name);
            Reload(selected.Id);
            StatusText = LocalizationService.Current.Format(LocationsKeys.LocationRenamed, name);
            onChanged?.Invoke();
        }
        catch (LocationApiException exception)
        {
            ErrorText = Describe(exception);
        }
    }

    private bool CanEditSelected() => SelectedLocation is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void DeleteLocation()
    {
        ErrorText = string.Empty;
        if (SelectedLocation is not { } selected)
        {
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

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void MergeLocations()
    {
        ErrorText = string.Empty;
        if (SelectedLocation is not { } source || MergeTarget is not { } target || source.Id == target.Id)
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

    private bool CanMerge() =>
        SelectedLocation is { } source && MergeTarget is { } target && source.Id != target.Id;

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
        RebuildMergeTargets();
        NotifyCommands();
    }

    private void RebuildMergeTargets()
    {
        MergeTargets.Clear();
        foreach (var location in Locations.Where(location => location.Id != SelectedLocation?.Id))
        {
            MergeTargets.Add(location);
        }

        if (MergeTarget is null || MergeTargets.All(target => target.Id != MergeTarget.Id))
        {
            MergeTarget = MergeTargets.FirstOrDefault();
        }
    }

    private void NotifyCommands()
    {
        RenameLocationCommand.NotifyCanExecuteChanged();
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
