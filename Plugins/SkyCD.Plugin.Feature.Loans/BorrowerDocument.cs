using SkyCD.Couchbase.Attributes;

namespace SkyCD.Plugin.Feature.Loans;

[CouchbaseDocument("borrowers", repositoryType: typeof(BorrowerDocumentRepository))]
public sealed class BorrowerDocument
{
    [Id] public string Id { get; init; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Comments { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Fax { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string WebPage { get; set; } = string.Empty;

    public string Messenger { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;
}
