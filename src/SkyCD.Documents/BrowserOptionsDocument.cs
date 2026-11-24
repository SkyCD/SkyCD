using System.Collections.Generic;
using SkyCD.UI.Controls.Lists;

namespace SkyCD.Documents;

public sealed class BrowserOptionsDocument
{
    public BrowserViewMode ViewMode { get; set; } = BrowserViewMode.Details;

    public string SortMode { get; set; } = "Name";

    /// <summary>Whether the saved browser sort runs from highest to lowest.</summary>
    public bool SortDescending { get; set; }

    public List<DetailsColumnSettingDocument> DetailsColumns { get; set; } = [];
}