using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("borrowers", repositoryType: typeof(BorrowerDocumentRepository))]
public sealed class BorrowerDocument
{
    [Id] public string Id { get; init; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
