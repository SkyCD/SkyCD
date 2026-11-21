using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// One row of the Custom properties grid. <see cref="DefaultValue"/> is the committed value the dialog
/// saves; while <see cref="IsEditing"/> is true the cell edits <see cref="EditValue"/> instead, so
/// cancelling can drop the draft without touching what Confirm would persist.
/// </summary>
public partial class CustomPropertyItemViewModel : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string defaultValue = string.Empty;

    /// <summary>True while the value cell shows an in-place editor.</summary>
    [ObservableProperty] private bool isEditing;

    /// <summary>Draft text shown by the in-place editor; only copied into <see cref="DefaultValue"/> on commit.</summary>
    [ObservableProperty] private string editValue = string.Empty;

    /// <summary>Starts editing from the committed value.</summary>
    public void BeginEdit()
    {
        if (IsEditing)
        {
            return;
        }

        EditValue = DefaultValue;
        IsEditing = true;
    }

    /// <summary>Copies the draft into <see cref="DefaultValue"/> (what Confirm saves) and leaves edit mode.</summary>
    public void CommitEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        DefaultValue = EditValue;
        IsEditing = false;
    }

    /// <summary>Drops the draft, keeps <see cref="DefaultValue"/> and leaves edit mode.</summary>
    public void CancelEdit() => IsEditing = false;
}
