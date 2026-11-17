using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("tags", repositoryType: typeof(TagDocumentRepository))]
public sealed class TagDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
