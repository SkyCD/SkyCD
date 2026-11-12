using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public static class CatalogFindService
{
    public static IReadOnlyList<CatalogFindResult> Search(IReadOnlyList<CatalogDocument> documents, string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || documents.Count == 0) return [];

        var byId = documents.Where(static document => !string.IsNullOrWhiteSpace(document.Id))
            .GroupBy(static document => document.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        return documents.Select(document => CreateResult(document, byId))
            .Where(result => result.FullPath.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(static result => result.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static result => result.Document.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static CatalogFindResult CreateResult(CatalogDocument document, IReadOnlyDictionary<string, CatalogDocument> byId)
    {
        var parts = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        CatalogDocument? current = document;
        while (current is not null && visited.Add(current.Id))
        {
            if (!string.IsNullOrWhiteSpace(current.Name)) parts.Push(current.Name);
            current = !string.IsNullOrWhiteSpace(current.ParentId) && byId.TryGetValue(current.ParentId, out var parent)
                ? parent
                : null;
        }

        var pathParts = parts.ToArray();
        var path = string.Join(" / ", pathParts);
        var context = pathParts.Length > 1 ? string.Join(" / ", pathParts[..^1]) : path;
        return new CatalogFindResult(document, path, context);
    }
}
