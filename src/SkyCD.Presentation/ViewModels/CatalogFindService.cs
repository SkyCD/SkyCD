using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public static class CatalogFindService
{
    public static IReadOnlyList<CatalogFindResult> Search(IReadOnlyList<CatalogDocument> documents, string? query) =>
        Search(documents, query, fieldProvider: null);

    /// <summary>
    /// Finds entries whose catalog path matches, and additionally entries whose contributed fields match.
    /// Field matching is delegated entirely to <paramref name="fieldProvider"/>, so the search never needs to
    /// know which plugin captured a value or what media format it describes.
    /// </summary>
    public static IReadOnlyList<CatalogFindResult> Search(
        IReadOnlyList<CatalogDocument> documents,
        string? query,
        ICatalogSearchFieldProvider? fieldProvider,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || documents.Count == 0) return [];

        var trimmed = query.Trim();
        var fieldLookup = fieldProvider?.OpenLookup();
        var byId = documents.Where(static document => !string.IsNullOrWhiteSpace(document.Id))
            .GroupBy(static document => document.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        return documents.Select(document =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CreateResult(document, byId, trimmed, fieldLookup);
            })
            .Where(static result => result is not null)
            .Select(static result => result!)
            .OrderBy(static result => result.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static result => result.Document.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static CatalogFindResult? CreateResult(
        CatalogDocument document,
        IReadOnlyDictionary<string, CatalogDocument> byId,
        string query,
        CatalogSearchFieldLookup? fieldLookup)
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

        if (path.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CatalogFindResult(document, path, context);
        }

        var matchedField = fieldLookup is null ? null : FindMatchingField(document, query, fieldLookup);
        return matchedField is null
            ? null
            : new CatalogFindResult(document, path, context) { MatchedField = matchedField };
    }

    private static string? FindMatchingField(
        CatalogDocument document,
        string query,
        CatalogSearchFieldLookup fieldLookup)
    {
        foreach (var field in fieldLookup(document))
        {
            if (Matches(field.Value, query) || Matches(field.Key, query))
            {
                return field.Key;
            }
        }

        return null;
    }

    private static bool Matches(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
