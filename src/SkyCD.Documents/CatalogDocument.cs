using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;

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

}
