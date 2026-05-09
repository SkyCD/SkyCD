using SkyCD.Documents.Enum;
namespace SkyCD.Documents;

public sealed class BrowserOptionsDocument
{
    public BrowserViewMode ViewMode { get; set; } = BrowserViewMode.Details;

    public BrowserSortMode SortMode { get; set; } = BrowserSortMode.Name;
}
