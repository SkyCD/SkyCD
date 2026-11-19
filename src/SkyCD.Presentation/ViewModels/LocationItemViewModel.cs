using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One location row for the manager dialog and picker UIs: the location id,
/// display name, and the number of catalog entries effectively using it.
/// </summary>
public sealed partial class LocationItemViewModel : ObservableObject
{
    public LocationItemViewModel(string locationId, string name, int usageCount = 0)
    {
        LocationId = locationId;
        this.name = name;
        UsageCount = usageCount;
    }

    public string LocationId { get; }

    [ObservableProperty]
    private string name;

    public int UsageCount { get; set; }
}
