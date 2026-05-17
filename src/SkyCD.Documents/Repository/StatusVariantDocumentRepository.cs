using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class StatusVariantDocumentRepository : RepositoryBase<StatusVariantDocument>
{
    public IReadOnlyList<StatusVariantDocument> GetOrdered()
    {
        return GetAll()
            .Where(static status => !string.IsNullOrWhiteSpace(status.Name))
            .OrderBy(static status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ReplaceAll(IEnumerable<StatusVariantDocument> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

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
        foreach (var status in statuses.Where(static status => !string.IsNullOrWhiteSpace(status.Name)))
        {
            var normalized = new StatusVariantDocument
            {
                Id = string.IsNullOrWhiteSpace(status.Id) ? $"status-{index++:D4}" : status.Id,
                Name = status.Name.Trim(),
                IconGlyph = status.IconGlyph.Trim()
            };
            Save(normalized.Id, normalized);
        }
    }
}
