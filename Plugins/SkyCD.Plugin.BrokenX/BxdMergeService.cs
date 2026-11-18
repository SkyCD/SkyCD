using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.BrokenX;

/// <summary>
/// Merges several imported Broken X documents into one deterministic row set.
/// </summary>
/// <remarks>
/// Rows are keyed by node ID. The first source wins every conflict and every conflict is
/// reported as a warning; nothing is silently overwritten. Rows whose parent link dangles
/// after the union get their intermediate folders synthesized so the result stays consistent.
/// </remarks>
public static class BxdMergeService
{
    /// <summary>
    /// Merges report row sets in source order.
    /// </summary>
    public static BxdReportDocument Merge(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var merged = new List<Dictionary<string, object?>>();
        var byId = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var totalRecords = 0;

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var rows = sources[sourceIndex];
            totalRecords += rows.Count;
            foreach (var row in rows)
            {
                var nodeId = row.TryGetValue("nodeId", out var idValue) ? idValue?.ToString() : null;
                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    warnings.Add($"Warning: source {sourceIndex} has a row without a node ID; skipped.");
                    continue;
                }

                if (!byId.TryAdd(nodeId, row))
                {
                    var existing = byId[nodeId];
                    var existingName = existing.TryGetValue("name", out var existingNameValue) ? existingNameValue?.ToString() : null;
                    var incomingName = row.TryGetValue("name", out var incomingNameValue) ? incomingNameValue?.ToString() : null;
                    var existingKind = existing.TryGetValue("kind", out var existingKindValue) ? existingKindValue?.ToString() : null;
                    var incomingKind = row.TryGetValue("kind", out var incomingKindValue) ? incomingKindValue?.ToString() : null;
                    if (!string.Equals(existingName, incomingName, StringComparison.Ordinal) ||
                        !string.Equals(existingKind, incomingKind, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Warning: '{nodeId}' differs between sources; kept the first occurrence.");
                    }

                    continue;
                }

                merged.Add(row);
            }
        }

        RepairDanglingParents(merged, byId, warnings);

        var summary = new BxdImportSummary(
            TotalRecordsRead: totalRecords,
            ImportedRecordsCount: merged.Count,
            SkippedRecordsCount: 0,
            IsPartialReport: warnings.Count > 0,
            SkippedReasons: warnings,
            UnsupportedFields: [],
            DetectedVersion: $"0x{BxdFormat.SupportedVersion:X2}");

        return new BxdReportDocument(merged, summary);
    }

    private static void RepairDanglingParents(
        List<Dictionary<string, object?>> merged,
        Dictionary<string, Dictionary<string, object?>> byId,
        List<string> warnings)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in merged)
        {
            var parentId = row.TryGetValue("parentId", out var parentValue) ? parentValue?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(parentId) && !byId.ContainsKey(parentId))
            {
                missing.Add(parentId);
            }
        }

        foreach (var parentId in missing)
        {
            // Synthesize from the node ID shape "<kind>:<media>/<rel...>" so the tree stays linked.
            var separator = parentId.IndexOf(':');
            var path = separator >= 0 ? parentId[(separator + 1)..] : parentId;
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var name = segments.Length > 0 ? segments[^1] : parentId;
            var synthesized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = parentId,
                ["parentId"] = segments.Length > 1
                    ? $"{(separator >= 0 ? parentId[..separator] : "folder")}:{string.Join("/", segments[..^1])}"
                    : null,
                ["kind"] = "folder",
                ["name"] = name,
                ["sizeBytes"] = "0",
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            };
            byId.Add(parentId, synthesized);
            merged.Add(synthesized);
            warnings.Add($"Warning: synthesized missing intermediate folder '{parentId}'.");
        }

        if (missing.Count > 0)
        {
            RepairDanglingParents(merged, byId, warnings);
        }
    }
}
