using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Repository;

namespace SkyCD.Documents;

[CouchbaseDocument("scan_exclusion_rules", repositoryType: typeof(ScanExclusionRuleDocumentRepository))]
public sealed class ScanExclusionRuleDocument
{
    [Id] public string Id { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    /// <summary>Entry kind the rule applies to: "File", "Folder" or "Both".</summary>
    public string EntryType { get; set; } = "Both";

    /// <summary>False for exclusion rules; true for include-by-mask (whitelist) rules.</summary>
    public bool IsInclude { get; set; }
}
