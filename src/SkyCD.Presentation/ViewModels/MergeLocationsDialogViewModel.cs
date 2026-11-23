using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Destination picker for the Locations merge dialog. Nothing is staged here: the Options view model
/// commits the merge only after the dialog is confirmed with a destination.
/// </summary>
public sealed partial class MergeLocationsDialogViewModel : ObservableObject
{
    public MergeLocationsDialogViewModel(
        LocationItemViewModel source,
        IEnumerable<LocationItemViewModel> destinations)
    {
        SourceName = source.Name;
        UsageCount = source.UsageCount;
        Destinations = [.. destinations];
        summary = LocalizationService.Current.Format(TextKeys.MergeLocationSummary, SourceName, UsageCount);
    }

    public string SourceName { get; }

    /// <summary>Number of entries whose assignment moves to the chosen destination.</summary>
    public int UsageCount { get; }

    public IReadOnlyList<LocationItemViewModel> Destinations { get; }

    [ObservableProperty] private string summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private LocationItemViewModel? selectedDestination;

    public bool CanConfirm => SelectedDestination is not null;
}
