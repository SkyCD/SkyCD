using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("collections", repositoryType: typeof(VirtualCollectionDocumentRepository))]
public sealed class VirtualCollectionDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public VirtualCollectionKind Kind { get; set; } = VirtualCollectionKind.Static;

    public List<string> EntryIds { get; set; } = [];

    public string? SavedQueryJson { get; set; }
}
