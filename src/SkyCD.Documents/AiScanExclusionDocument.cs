using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("ai_scan_exclusions", repositoryType: typeof(AiScanExclusionDocumentRepository))]
public sealed class AiScanExclusionDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    /// <summary>MIME type excluded from AI scanning, for example <c>application/zip</c>.</summary>
    public string MimeType { get; set; } = string.Empty;
}
