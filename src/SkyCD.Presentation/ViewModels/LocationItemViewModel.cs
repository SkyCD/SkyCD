using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>One location row for the add-to-list and media-scan pickers.</summary>
public sealed partial class LocationItemViewModel : ObservableObject
{
    public LocationItemViewModel(string locationId, string name)
    {
        LocationId = locationId;
        this.name = name;
    }

    public string LocationId { get; }

    [ObservableProperty]
    private string name;
}
