using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class ScanExclusionRuleDocumentRepository : RepositoryBase<ScanExclusionRuleDocument>
{
    public IReadOnlyList<ScanExclusionRuleDocument> CreateDefaultEntries()
    {
        return WellKnownScanExclusions.GetDefaultExclusionRules();
    }

    public void EnsureDefaultRules()
    {
        if (GetAll().Count > 0)
        {
            return;
        }

        ReplaceAll(CreateDefaultEntries());
    }

    public IReadOnlyList<ScanExclusionRuleDocument> GetOrdered()
    {
        return GetAll()
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .OrderBy(static rule => rule.IsInclude)
            .ThenBy(static rule => rule.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ReplaceAll(IEnumerable<ScanExclusionRuleDocument> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var existing in GetAll())
        {
            if (string.IsNullOrWhiteSpace(existing.Id))
            {
                continue;
            }

            using var document = Collection.GetDocument(existing.Id);
            if (document is not null)
            {
                Collection.Delete(document);
            }
        }

        var index = 0;
        foreach (var rule in rules.Where(static rule => !string.IsNullOrWhiteSpace(rule.Pattern)))
        {
            var normalized = new ScanExclusionRuleDocument
            {
                Id = string.IsNullOrWhiteSpace(rule.Id) ? $"scan-rule-{index++:D4}" : rule.Id,
                Pattern = rule.Pattern.Trim(),
                EntryType = rule.EntryType is "File" or "Folder" or "Both" ? rule.EntryType : "Both",
                IsInclude = rule.IsInclude
            };
            Save(normalized.Id, normalized);
        }
    }
}
