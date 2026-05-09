using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Formatting;

namespace SkyCD.Documents;

[CouchbaseDocument("catalog", repositoryType: typeof(CatalogDocumentRepository))]
public sealed class CatalogDocument
{
    [Id]
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    [ParentId]
    public string? ParentId { get; init; }

    public CatalogDocumentType Type { get; init; } = CatalogDocumentType.File;

    public long Size { get; init; }

    public long ChildrenCount { get; init; }

    public PropertiesCollection Properties { get; init; } = new();

    public string DisplayType => Type.ToDisplayName();

    public string DisplaySize => SizeFormatting.FormatBytes(Size, "0.##");

    public string IconGlyph => Type.ResolveIconGlyph();
}
