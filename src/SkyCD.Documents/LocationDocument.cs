using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("locations", repositoryType: typeof(LocationDocumentRepository))]
public sealed class LocationDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
