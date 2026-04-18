namespace SkyCD.Domain.Catalogs;

public sealed class CatalogNode
{
    public long Id { get; set; }

    public Guid CatalogId { get; set; }

    public Catalog? Catalog { get; set; }

    public long? ParentId { get; set; }

    public CatalogNode? Parent { get; set; }

    public ICollection<CatalogNode> Children { get; } = new List<CatalogNode>();

    public CatalogNodeKind Kind { get; set; } = CatalogNodeKind.Folder;

    public string Name { get; set; } = string.Empty;

    public long? SizeBytes { get; set; }

    public string? MimeType { get; set; }

    public DateTimeOffset? LastModifiedUtc { get; set; }

    public string? MetadataJson { get; set; }

    public bool IsRoot => ParentId is null;
}
