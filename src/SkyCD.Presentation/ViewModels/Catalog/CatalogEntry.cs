namespace SkyCD.Presentation.ViewModels.Catalog;

public sealed class CatalogEntry
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? ParentId { get; init; }

    public CatalogEntryType Type { get; init; }

    public long Size { get; init; }

    public long ChildrenCount { get; init; }
}
