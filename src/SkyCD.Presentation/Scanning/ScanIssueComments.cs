using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Writes scan issues into the comment of the affected catalog entry. Entries are matched by their
/// recorded full name first, then by a unique file name when the scan only reported a name.
/// </summary>
public static class ScanIssueComments
{
    public const string PropertyKey = "Comment";

    public static IReadOnlyList<CatalogDocument> Apply(IReadOnlyList<CatalogDocument> documents,
        IReadOnlyList<ScanIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(issues);

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var byFullName = new Dictionary<string, CatalogDocument>(pathComparer);
        var byName = new Dictionary<string, List<CatalogDocument>>(pathComparer);
        foreach (var document in documents)
        {
            if (TryGetFullName(document) is { Length: > 0 } fullName)
            {
                byFullName.TryAdd(fullName, document);
            }

            if (!byName.TryGetValue(document.Name, out var matches))
            {
                matches = [];
                byName[document.Name] = matches;
            }

            matches.Add(document);
        }

        var applied = new List<CatalogDocument>();
        foreach (var issue in issues)
        {
            if (string.IsNullOrWhiteSpace(issue.ItemPath) || string.IsNullOrWhiteSpace(issue.Message))
            {
                continue;
            }

            var document = ResolveDocument(issue.ItemPath, byFullName, byName);
            if (document is null)
            {
                continue;
            }

            var comment = document.Properties.TryGetValue(PropertyKey, out var existing)
                ? existing?.ToString() ?? string.Empty
                : string.Empty;
            if (comment.Contains(issue.Message, StringComparison.Ordinal))
            {
                continue;
            }

            document.Properties[PropertyKey] = comment.Length == 0
                ? issue.Message
                : comment + Environment.NewLine + issue.Message;
            if (!applied.Contains(document))
            {
                applied.Add(document);
            }
        }

        return applied;
    }

    private static CatalogDocument? ResolveDocument(string itemPath,
        Dictionary<string, CatalogDocument> byFullName,
        Dictionary<string, List<CatalogDocument>> byName)
    {
        if (byFullName.TryGetValue(itemPath, out var byPath))
        {
            return byPath;
        }

        var name = Path.GetFileName(itemPath);
        if (string.IsNullOrEmpty(name))
        {
            name = itemPath;
        }

        return byName.TryGetValue(name, out var candidates) && candidates.Count == 1 ? candidates[0] : null;
    }

    private static string? TryGetFullName(CatalogDocument document) =>
        document.Properties.TryGetValue("FullName", out var value) ? value?.ToString() : null;
}
