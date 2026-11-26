namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

/// <summary>
/// One pack tab of the icon picker. The tab shows a representative icon of the pack instead of its
/// name; the label stays available for automation and the tooltip.
/// </summary>
public sealed class IconCategoryItem
{
    public IconCategoryItem(string label, string iconKey, int startIndex)
    {
        Label = label;
        IconKey = iconKey;
        StartIndex = startIndex;
    }

    public string Label { get; }

    public string IconKey { get; }

    /// <summary>Index of the pack's first icon in the full, unfiltered sheet.</summary>
    public int StartIndex { get; }
}
