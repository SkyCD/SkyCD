using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One tag row for assignment UIs: the manager dialog (with usage counts)
/// and the Properties dialog Tags tab (two-state checkboxes).
/// </summary>
public sealed partial class TagAssignmentViewModel : ObservableObject
{
    public TagAssignmentViewModel(string tagId, string name, int usageCount = 0, bool assigned = false)
    {
        TagId = tagId;
        Name = name;
        UsageCount = usageCount;
        IsAssigned = assigned;
    }

    public string TagId { get; }

    public string Name { get; set; }

    public int UsageCount { get; set; }

    [ObservableProperty]
    private bool isAssigned;
}
