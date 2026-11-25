using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Applies <see cref="CatalogSizeRecalculator"/> to a stored or in-memory catalog after entries moved.
/// </summary>
public static class CatalogSizeMaintenance
{
    // The read-and-write pass runs as one gated section: a recount that read a snapshot an add already
    // superseded would otherwise persist stale totals over it.

    /// <summary>
    /// Lists the parent ids of <paramref name="documents"/> that point outside the given set, which is what a
    /// restored or removed subtree has to anchor its recount on: parents inside the set are restored from the
    /// snapshot itself and must not be recomputed.
    /// </summary>
    public static IReadOnlyList<string> ExternalParentIds(IReadOnlyCollection<CatalogDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!string.IsNullOrWhiteSpace(document.Id))
            {
                ids.Add(document.Id);
            }
        }

        var parents = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var parentId = document.ParentId;
            if (!string.IsNullOrWhiteSpace(parentId) && !ids.Contains(parentId) && seen.Add(parentId))
            {
                parents.Add(parentId);
            }
        }

        return parents;
    }

    /// <summary>
    /// Recounts the given parents and their ancestors in the repository and persists every entry whose size
    /// changed. Returns the persisted replacements.
    /// </summary>
    public static IReadOnlyList<CatalogDocument> RecalculateAncestors(
        CatalogDocumentRepository repository,
        IEnumerable<string?> parentIds)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(parentIds);

        var anchors = parentIds.Where(static id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (anchors.Length == 0)
        {
            return [];
        }

        return DatabaseGate.Run(repository.Collection.Database, () =>
        {
            var documents = repository.GetAll();
            var updated = CatalogSizeRecalculator.Recalculate(documents,
                CatalogSizeRecalculator.ResolveAncestorIds(documents, anchors));
            if (updated.Count == 0)
            {
                return updated;
            }

            DatabaseGate.InBatch(repository.Collection.Database, () =>
            {
                foreach (var document in updated)
                {
                    repository.Save(document.Id, document);
                }
            });

            return updated;
        });
    }

    /// <summary>
    /// Recounts the given parents and their ancestors in an in-memory entry list, replacing the entries that
    /// changed. Returns the replacements.
    /// </summary>
    public static IReadOnlyList<CatalogDocument> RecalculateAncestors(
        IList<CatalogDocument> entries,
        IEnumerable<string?> parentIds)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(parentIds);

        var anchors = parentIds.Where(static id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (entries.Count == 0 || anchors.Length == 0)
        {
            return [];
        }

        var snapshot = entries.ToArray();
        var updated = CatalogSizeRecalculator.Recalculate(snapshot,
            CatalogSizeRecalculator.ResolveAncestorIds(snapshot, anchors));
        if (updated.Count == 0)
        {
            return updated;
        }

        var byId = updated.ToDictionary(static document => document.Id, StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            if (byId.TryGetValue(entries[index].Id, out var replacement))
            {
                entries[index] = replacement;
            }
        }

        return updated;
    }
}
