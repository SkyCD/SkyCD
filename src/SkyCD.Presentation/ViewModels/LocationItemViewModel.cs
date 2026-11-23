using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One location row for the manager and picker UIs: the location id, display name, and the number of
/// catalog entries effectively using it. Rows created in the Options dialog before saving are not
/// stored yet and carry a temporary id.
/// </summary>
public sealed partial class LocationItemViewModel : ObservableObject
{
    public LocationItemViewModel(string locationId, string name, int usageCount = 0, bool isStored = true)
    {
        LocationId = locationId;
        this.name = name;
        UsageCount = usageCount;
        IsStored = isStored;
    }

    public string LocationId { get; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private int usageCount;

    /// <summary>True while the name cell shows an in-place editor.</summary>
    [ObservableProperty]
    private bool isEditing;

    /// <summary>Draft text shown by the in-place editor; only copied into <see cref="Name"/> on commit.</summary>
    [ObservableProperty]
    private string editName = string.Empty;

    /// <summary>False for a row created in the Options dialog that only exists once the dialog is saved.</summary>
    public bool IsStored { get; }

    /// <summary>The name the row's pending creation currently carries in the staged change list.</summary>
    public string? PendingCreateName { get; set; }

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
