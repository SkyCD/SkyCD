using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Locations.Models;

/// <summary>
/// One stored location with the number of entries whose effective location it is. A row created in the
/// Options page may be unstored while its name is being entered; it is only persisted when the inline
/// editor commits.
/// </summary>
public sealed partial class LocationRow : ObservableObject
{
    public LocationRow(string id, string name, int usageCount, bool isStored = true)
    {
        Id = id;
        this.name = name;
        UsageCount = usageCount;
        IsStored = isStored;
    }

    public string Id { get; }

    public int UsageCount { get; }

    public bool IsStored { get; }

    [ObservableProperty] private string name;

    [ObservableProperty] private bool isEditing;

    [ObservableProperty] private string editName = string.Empty;

    public string UsageText => LocalizationService.Current.Format(LocationsKeys.UsageCount, UsageCount);

    /// <summary>Starts editing from the committed name.</summary>
    public void BeginEdit()
    {
        if (IsEditing)
        {
            return;
        }

        EditName = Name;
        IsEditing = true;
    }

    /// <summary>Drops the draft, keeps <see cref="Name"/> and leaves edit mode.</summary>
    public void CancelEdit()
    {
        EditName = Name;
        IsEditing = false;
    }
}
