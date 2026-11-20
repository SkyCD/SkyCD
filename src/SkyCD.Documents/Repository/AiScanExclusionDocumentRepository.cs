using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class AiScanExclusionDocumentRepository : RepositoryBase<AiScanExclusionDocument>
{
    public IReadOnlyList<AiScanExclusionDocument> CreateDefaultEntries()
    {
        return WellKnownAiScanExclusions.GetDefaultExcludedMimeTypes()
            .Select((mimeType, index) => new AiScanExclusionDocument
            {
                Id = $"ai-mime-exclusion-{index:D4}",
                MimeType = mimeType
            })
            .ToArray();
    }

    public void EnsureDefaultEntries()
    {
        if (GetAll().Count > 0)
        {
            return;
        }

        ReplaceAll(CreateDefaultEntries());
    }

    public IReadOnlyList<AiScanExclusionDocument> GetOrdered()
    {
        return GetAll()
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.MimeType))
            .OrderBy(static entry => entry.MimeType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ReplaceAll(IEnumerable<AiScanExclusionDocument> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

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
        foreach (var entry in entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.MimeType)))
        {
            var normalized = new AiScanExclusionDocument
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? $"ai-mime-exclusion-{index++:D4}" : entry.Id,
                MimeType = entry.MimeType.Trim()
            };
            Save(normalized.Id, normalized);
        }
    }
}
