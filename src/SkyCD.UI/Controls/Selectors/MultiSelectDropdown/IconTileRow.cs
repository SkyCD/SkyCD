using System;
using System.Collections.Generic;

namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

/// <summary>
/// One horizontally laid out row of the virtualized icon sheet. Only visible rows are realized,
/// so a catalog of tens of thousands of icons still opens instantly (issue #1214).
/// </summary>
public sealed class IconTileRow
{
    public IconTileRow(IconTileItem[] source, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        StartIndex = startIndex;
        Tiles = new ArraySegment<IconTileItem>(source, startIndex, count);
    }

    /// <summary>Index of the first tile of this row in the visible tile list.</summary>
    public int StartIndex { get; }

    public IReadOnlyList<IconTileItem> Tiles { get; }
}
