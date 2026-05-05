using System;
using System.Collections.Generic;

namespace SkyCD.Domain.Catalogs;

public sealed class Catalog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<CatalogNode> Nodes { get; } = new List<CatalogNode>();

    public ICollection<CatalogTag> Tags { get; } = new List<CatalogTag>();
}
