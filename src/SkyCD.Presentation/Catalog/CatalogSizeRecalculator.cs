using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Recomputes stored folder and media sizes from their children. Sizes are subtree totals, so a parent is
/// the sum of its direct children; children are processed first so a parent never folds in a stale child.
/// </summary>
/// <remarks>
/// Unknown sizes propagate upwards: a parent with a child of unknown size is unknown itself, because the
/// total cannot be stated. An entry that stays empty keeps whatever it already had — an unscanned manual
/// folder stays unknown, while a folder whose contents were known and are now all deleted becomes a real
/// zero.
/// </remarks>
public static class CatalogSizeRecalculator
{
    /// <summary>
    /// Expands the given parent ids into that parent and every ancestor inside <paramref name="documents"/>.
    /// Ids that are not part of the snapshot are skipped, since their parent chain cannot be followed.
    /// </summary>
    public static IReadOnlyList<string> ResolveAncestorIds(
        IReadOnlyList<CatalogDocument> documents,
        IEnumerable<string?> parentIds)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(parentIds);

        var byId = IndexById(documents);
        var ancestors = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();

        foreach (var parentId in parentIds)
        {
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                pending.Push(parentId);
            }
        }

        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!visited.Add(id) || !byId.TryGetValue(id, out var document))
            {
                continue;
            }

            ancestors.Add(id);
            if (!string.IsNullOrWhiteSpace(document.ParentId))
            {
                pending.Push(document.ParentId);
            }
        }

        return ancestors;
    }

    /// <summary>
    /// Returns a copy of every entry among <paramref name="parentIds"/> whose size differs once its children
    /// are summed. The result is ordered children-first, so persisting it in order keeps the stored tree
    /// consistent after each write.
    /// </summary>
    public static IReadOnlyList<CatalogDocument> Recalculate(
        IReadOnlyList<CatalogDocument> documents,
        IReadOnlyCollection<string> parentIds)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(parentIds);

        if (documents.Count == 0 || parentIds.Count == 0)
        {
            return [];
        }

        var byId = IndexById(documents);
        var childIdsByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Id) || string.IsNullOrWhiteSpace(document.ParentId))
            {
                continue;
            }

            if (!childIdsByParent.TryGetValue(document.ParentId, out var children))
            {
                children = [];
                childIdsByParent[document.ParentId] = children;
            }

            children.Add(document.Id);
        }

        var ordered = parentIds
            .Distinct(StringComparer.Ordinal)
            .Where(byId.ContainsKey)
            .Select(id => (Id: id, Depth: DepthOf(id, byId)))
            .OrderByDescending(entry => entry.Depth)
            .Select(entry => entry.Id);

        var updated = new List<CatalogDocument>();
        foreach (var id in ordered)
        {
            var document = byId[id];
            var children = childIdsByParent.TryGetValue(id, out var childIdList)
                ? childIdList.Select(childId => byId[childId]).ToArray()
                : [];

            var recalculated = RecalculateOne(document, children);
            if (recalculated is null)
            {
                continue;
            }

            byId[id] = recalculated;
            updated.Add(recalculated);
        }

        return updated;
    }

    private static CatalogDocument? RecalculateOne(CatalogDocument document, IReadOnlyList<CatalogDocument> children)
    {
        if (children.Count == 0)
        {
            // Nothing to sum. An unknown entry stays unknown (an empty manual folder is not a known zero);
            // a known one is genuinely empty and must not keep a stale total from its deleted contents.
            return document.HasKnownSize && document.Size != 0
                ? document.WithSize(0, sizeKnown: true)
                : null;
        }

        if (children.All(child => child.HasKnownSize))
        {
            var total = 0L;
            foreach (var child in children)
            {
                total += child.Size;
            }

            return document.HasKnownSize && document.Size == total
                ? null
                : document.WithSize(total, sizeKnown: true);
        }

        return document.HasKnownSize
            ? document.WithSize(document.Size, sizeKnown: false)
            : null;
    }

    private static Dictionary<string, CatalogDocument> IndexById(IReadOnlyList<CatalogDocument> documents)
    {
        var byId = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!string.IsNullOrWhiteSpace(document.Id))
            {
                byId.TryAdd(document.Id, document);
            }
        }

        return byId;
    }

    private static int DepthOf(string id, IReadOnlyDictionary<string, CatalogDocument> byId)
    {
        var depth = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal) { id };
        var current = id;
        while (byId.TryGetValue(current, out var document) &&
               !string.IsNullOrWhiteSpace(document.ParentId) &&
               visited.Add(document.ParentId))
        {
            depth++;
            current = document.ParentId;
        }

        return depth;
    }
}
