using SkyCD.Presentation.ViewModels;

namespace SkyCD.Documents;

public sealed class BrowserOptionsDocument
{
    public BrowserViewMode ViewMode { get; set; } = BrowserViewMode.Details;

    public BrowserSortMode SortMode { get; set; } = BrowserSortMode.Name;
}
