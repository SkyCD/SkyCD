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
                IconGlyph = "Lucide:Check",
                IconColor = "#22C55E",
                ItemTypes = [CatalogDocumentType.Media]
            },
            new StatusVariantDocument
            {
                Id = "status-in-progress",
                Name = "In Progress",
                IconGlyph = "Lucide:Clock3",
                IconColor = "#F59E0B",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-completed",
                Name = "Completed",
                IconGlyph = "Lucide:CheckCheck",
                IconColor = "#10B981",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-favorites",
                Name = "Favorite",
                IconGlyph = "Lucide:Star",
                IconColor = "#EAB308",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-backlog",
                Name = "Backlog",
                IconGlyph = "Lucide:ListTodo",
                IconColor = "#38BDF8",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-on-hold",
                Name = "On Hold",
                IconGlyph = "Lucide:PauseCircle",
                IconColor = "#A78BFA",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-dropped",
                Name = "Dropped",
                IconGlyph = "Lucide:XCircle",
                IconColor = "#EF4444",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-plan-to-watch",
                Name = "Plan to Watch",
                IconGlyph = "Lucide:CalendarPlus",
                IconColor = "#60A5FA",
                ItemTypes = [CatalogDocumentType.Media]
            },
            new StatusVariantDocument
            {
                Id = "status-rewatching",
                Name = "Rewatching",
                IconGlyph = "Lucide:RotateCcw",
                IconColor = "#14B8A6",
                ItemTypes = [CatalogDocumentType.Media]
            },
            new StatusVariantDocument
            {
                Id = "status-owned",
                Name = "Owned",
                IconGlyph = "Lucide:PackageCheck",
                IconColor = "#84CC16",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-loaned-out",
                Name = "Loaned Out",
                IconGlyph = "Lucide:Handshake",
                IconColor = "#F97316",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-missing",
                Name = "Missing",
                IconGlyph = "Lucide:Search",
                IconColor = "#FB7185",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
            },
            new StatusVariantDocument
            {
                Id = "status-needs-repair",
                Name = "Needs Repair",
                IconGlyph = "Lucide:TriangleAlert",
                IconColor = "#F59E0B",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File, CatalogDocumentType.Folder]
            },
            new StatusVariantDocument
            {
                Id = "status-archived",
                Name = "Archived",
                IconGlyph = "Lucide:Archive",
                IconColor = "#94A3B8",
                ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File, CatalogDocumentType.Folder]
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
                IconColor = string.IsNullOrWhiteSpace(status.IconColor) ? "#FFFFFF" : status.IconColor.Trim(),
                ItemTypes = (status.ItemTypes is { Count: > 0 }
                    ? status.ItemTypes
                    : null)?
                    .Distinct()
                    .ToArray()
            };
            Save(normalized.Id, normalized);
        }
    }
}
