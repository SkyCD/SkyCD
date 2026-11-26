using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Plugin.Feature.Locations.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Locations.Views;

/// <summary>
/// Destination picker for the Locations merge dialog. Nothing is merged here; the page view model
/// commits the merge only after the dialog is confirmed with a destination.
/// </summary>
public sealed partial class MergeLocationsDialogViewModel : ObservableObject
{
    public MergeLocationsDialogViewModel(LocationRow source, IEnumerable<LocationRow> destinations)
    {
        SourceName = source.Name;
        UsageCount = source.UsageCount;
        Destinations = [.. destinations];
        summary = LocalizationService.Current.Format(LocationsKeys.MergeSummary, SourceName, UsageCount);
    }

    public string SourceName { get; }

    public int UsageCount { get; }

    public IReadOnlyList<LocationRow> Destinations { get; }

    [ObservableProperty] private string summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private LocationRow? selectedDestination;

    public bool CanConfirm => SelectedDestination is not null;
}
