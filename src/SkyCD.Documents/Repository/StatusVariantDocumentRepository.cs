using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents.Enum;

namespace SkyCD.Documents.Repository;

public sealed class StatusVariantDocumentRepository : RepositoryBase<StatusVariantDocument>
{
    public IReadOnlyList<StatusVariantDocument> CreateDefaultEntries()
    {
        return
        [
            new StatusVariantDocument
            {
                Id = "status-watched",
                Name = "Watched",
                IconGlyph = "check",
                ItemType = CatalogDocumentType.Media
            },
            new StatusVariantDocument
            {
                Id = "status-favorite",
                Name = "Favorite",
                IconGlyph = "star",
                ItemType = CatalogDocumentType.Media
            },
            new StatusVariantDocument
            {
                Id = "status-important",
                Name = "Important",
                IconGlyph = "warning",
                ItemType = CatalogDocumentType.Folder
            }
        ];
    }

    public void EnsureDefaultStatuses()
    {
        if (GetAll().Count > 0)
        {
            return;
        }

        ReplaceAll(CreateDefaultEntries());
    }

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
                IconGlyph = status.IconGlyph.Trim(),
                ItemType = status.ItemType
            };
            Save(normalized.Id, normalized);
        }
    }
}
