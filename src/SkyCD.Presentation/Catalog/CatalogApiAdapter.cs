using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Catalog;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Projects the open host catalog into the domain-neutral <see cref="CatalogEntry"/> model so a plugin can
/// read entries without referencing the host document type.
/// </summary>
public sealed class CatalogApiAdapter(OpenCatalogState state) : ICatalogApi
{
    /// <inheritdoc />
    public string? OpenedCatalogPath => state.Path;

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var documents = state.Repository?.GetAll() ?? [];
        var byId = documents.ToDictionary(static document => document.Id, StringComparer.Ordinal);
        IReadOnlyList<CatalogEntry> entries =
            [.. documents.Select(document => Project(document, byId))];
        return Task.FromResult(entries);
    }

    /// <inheritdoc />
    public Task<CatalogEntry?> GetEntryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        var repository = state.Repository;
        var document = repository?.Get(id);
        if (document is null)
        {
            return Task.FromResult<CatalogEntry?>(null);
        }

        var byId = repository!.GetAll()
            .ToDictionary(static item => item.Id, StringComparer.Ordinal);
        return Task.FromResult<CatalogEntry?>(Project(document, byId));
    }

    /// <summary>Projects a stored document into the domain-neutral plugin read model.</summary>
    public static CatalogEntry Project(CatalogDocument document, IReadOnlyDictionary<string, CatalogDocument> byId) =>
        new(
            document.Id,
            document.Name,
            BuildPath(document, byId),
            document.ParentId,
            MapKind(document.Type),
            document.Size,
            document.Properties.ToDictionary(
                static property => property.Key,
                static property => property.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal));

    private static string BuildPath(CatalogDocument document, IReadOnlyDictionary<string, CatalogDocument> byId)
    {
        var segments = new List<string>();
        var current = document;
        for (var depth = 0; current is not null && depth < 256; depth++)
        {
            segments.Add(current.Name);
            current = current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        segments.Reverse();
        return "/" + string.Join("/", segments);
    }

    private static CatalogEntryKind MapKind(CatalogDocumentType type) => type switch
    {
        CatalogDocumentType.Folder or CatalogDocumentType.NetworkFolder => CatalogEntryKind.Folder,
        CatalogDocumentType.File or CatalogDocumentType.Media => CatalogEntryKind.File,
        _ => CatalogEntryKind.Other
    };
}
