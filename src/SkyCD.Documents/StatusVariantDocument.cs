using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using System.Collections.Generic;

namespace SkyCD.Documents;

[CouchbaseDocument("statuses", typeof(StatusVariantDocumentRepository))]
public sealed class StatusVariantDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string IconGlyph { get; set; } = string.Empty;

    public string IconColor { get; set; } = "#FFFFFF";

    public IReadOnlyList<CatalogDocumentType>? ItemTypes { get; set; }
}
