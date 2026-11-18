namespace SkyCD.UI.Controls.Preview;

/// <summary>
/// One typed metadata field as the preview surface displays it. The presentation layer projects stored
/// plugin values into rows, so the control never parses media-specific data itself.
/// </summary>
/// <param name="Label">Localized field name.</param>
/// <param name="Value">Formatted value, already culture- and unit-aware.</param>
/// <param name="Hint">Provenance shown next to the value, or null when the value is unremarkable.</param>
/// <param name="IsStale">True when the value comes from a record that is no longer current.</param>
/// <param name="IsUserEdited">True when a user annotation replaced the captured value.</param>
/// <param name="IsFailed">True when extraction failed and no value could be stored.</param>
/// <param name="IsCopyable">True when the value can be copied to the clipboard.</param>
public sealed record MediaPreviewFieldRow(
    string Label,
    string Value,
    string? Hint,
    bool IsStale,
    bool IsUserEdited,
    bool IsFailed = false,
    bool IsCopyable = true)
{
    /// <summary>Drives hint visibility without a converter, so the row stays self-describing.</summary>
    public bool HasHint => !string.IsNullOrEmpty(Hint);
}
