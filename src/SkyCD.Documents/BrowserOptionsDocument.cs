using System.Collections.Generic;
using SkyCD.UI.Controls.Lists;

namespace SkyCD.Documents;

public sealed class BrowserOptionsDocument
{
    public BrowserViewMode ViewMode { get; set; } = BrowserViewMode.Details;

    public string SortMode { get; set; } = "Name";

    public List<DetailsColumnSettingDocument> DetailsColumns { get; set; } = [];
}