using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.Search;

/// <summary>
/// In-memory catalog search index. Built from active catalog data (not just loaded UI nodes)
/// and kept consistent through <see cref="Upsert"/>/<see cref="Remove"/> after edits and imports,
/// so re-evaluation always observes current data. Indexes cover identity, hierarchy, type,
/// extension and status; arbitrary predicates fall back to per-entry evaluation.
/// </summary>
public sealed class CatalogSearchIndex
{
    private readonly Dictionary<string, CatalogDocument> byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> childrenByParent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> fullPathById = new(StringComparer.Ordinal);
    private readonly Dictionary<CatalogDocumentType, HashSet<string>> idsByType = new();
    private readonly Dictionary<string, HashSet<string>> idsByExtension = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> idsByStatus = new(StringComparer.OrdinalIgnoreCase);
    private bool pathsStale = true;

    public int Count => byId.Count;

    public void Rebuild(IEnumerable<CatalogDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Clear();
        foreach (var document in documents)
        {
            AddCore(document);
        }

        pathsStale = true;
    }

    public void Upsert(CatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Id);
        RemoveCore(document.Id);
        AddCore(document);
        pathsStale = true;
    }

    public bool Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var removed = RemoveCore(id);
        if (removed)
        {
            pathsStale = true;
        }

        return removed;
    }

    public void Clear()
    {
        byId.Clear();
        childrenByParent.Clear();
        fullPathById.Clear();
        idsByType.Clear();
        idsByExtension.Clear();
        idsByStatus.Clear();
        pathsStale = true;
    }

    public bool TryGet(string id, out CatalogDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return byId.TryGetValue(id, out document!);
    }

    public string GetFullPath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        EnsurePaths();
        return fullPathById.TryGetValue(id, out var path) ? path : string.Empty;
    }

    internal IReadOnlyList<CatalogDocument> GetScopeCandidates(CatalogQueryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.RootId is null)
        {
            return FilterByTypes(byId.Values, scope.Types);
        }

        if (!byId.ContainsKey(scope.RootId))
        {
            return [];
        }

        if (!scope.IncludeDescendants)
        {
            return FilterByTypes([byId[scope.RootId]], scope.Types);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal) { scope.RootId };
        var stack = new Stack<string>();
        stack.Push(scope.RootId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (ids.Add(child))
                {
                    stack.Push(child);
                }
            }
        }

        var ordered = ids.Select(id => byId[id]).ToList();
        return FilterByTypes(ordered, scope.Types);
    }

    private static IReadOnlyList<CatalogDocument> FilterByTypes(
        IEnumerable<CatalogDocument> source,
        IReadOnlyList<CatalogDocumentType>? types)
    {
        if (types is null || types.Count == 0)
        {
            return source.ToList();
        }

        var allowed = new HashSet<CatalogDocumentType>(types);
        return source.Where(document => allowed.Contains(document.Type)).ToList();
    }

    private void AddCore(CatalogDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            return;
        }

        byId[document.Id] = document;
        if (!string.IsNullOrWhiteSpace(document.ParentId))
        {
            if (!childrenByParent.TryGetValue(document.ParentId, out var children))
            {
                children = [];
                childrenByParent[document.ParentId] = children;
            }

            children.Add(document.Id);
        }

        if (!idsByType.TryGetValue(document.Type, out var typeIds))
        {
            typeIds = new HashSet<string>(StringComparer.Ordinal);
            idsByType[document.Type] = typeIds;
        }

        typeIds.Add(document.Id);
        idsByExtension.TryAdd(NormalizeExtension(document.Name), new HashSet<string>(StringComparer.Ordinal));
        idsByExtension[NormalizeExtension(document.Name)].Add(document.Id);
        var statusKey = (document.Status ?? string.Empty).Trim();
        if (statusKey.Length > 0)
        {
            if (!idsByStatus.TryGetValue(statusKey, out var statusIds))
            {
                statusIds = new HashSet<string>(StringComparer.Ordinal);
                idsByStatus[statusKey] = statusIds;
            }

            statusIds.Add(document.Id);
        }
    }

    private bool RemoveCore(string id)
    {
        if (!byId.TryGetValue(id, out var existing))
        {
            return false;
        }

        byId.Remove(id);
        if (!string.IsNullOrWhiteSpace(existing.ParentId) &&
            childrenByParent.TryGetValue(existing.ParentId, out var siblings))
        {
            siblings.Remove(id);
        }

        childrenByParent.Remove(id);
        if (idsByType.TryGetValue(existing.Type, out var typeIds))
        {
            typeIds.Remove(id);
        }

        var extension = NormalizeExtension(existing.Name);
        if (idsByExtension.TryGetValue(extension, out var extensionIds))
        {
            extensionIds.Remove(id);
        }

        var statusKey = (existing.Status ?? string.Empty).Trim();
        if (statusKey.Length > 0 && idsByStatus.TryGetValue(statusKey, out var statusIds))
        {
            statusIds.Remove(id);
        }

        fullPathById.Remove(id);
        return true;
    }

    private void EnsurePaths()
    {
        if (!pathsStale)
        {
            return;
        }

        fullPathById.Clear();
        foreach (var document in byId.Values)
        {
            fullPathById[document.Id] = BuildFullPath(document);
        }

        pathsStale = false;
    }

    private string BuildFullPath(CatalogDocument document)
    {
        var parts = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = document;
        while (current is not null && visited.Add(current.Id))
        {
            if (!string.IsNullOrWhiteSpace(current.Name))
            {
                parts.Push(current.Name);
            }

            current = !string.IsNullOrWhiteSpace(current.ParentId) && byId.TryGetValue(current.ParentId, out var parent)
                ? parent
                : null;
        }

        return string.Join(" / ", parts);
    }

    internal static string NormalizeExtension(string? name)
    {
        var extension = Path.GetExtension(name ?? string.Empty);
        return extension.Length <= 1 ? string.Empty : extension.Substring(1).ToLowerInvariant();
    }
}
