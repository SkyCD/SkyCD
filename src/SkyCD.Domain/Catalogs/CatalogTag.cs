namespace SkyCD.Domain.Catalogs;

public sealed class CatalogTag
{
    public long Id { get; set; }

    public Guid CatalogId { get; set; }

    public Catalog? Catalog { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
