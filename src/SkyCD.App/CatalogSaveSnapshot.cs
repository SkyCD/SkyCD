using System;
using System.Collections.Generic;
using SkyCD.Documents;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.App;

/// <summary>
/// Builds the single catalog snapshot consumed by both the loss assessment and the write, so the
/// warning always describes exactly what the writer receives.
/// </summary>
internal static class CatalogSaveSnapshot
{
    public static List<Dictionary<string, object?>> BuildRows(IReadOnlyList<CatalogDocument> documents)
    {
        var rows = new List<Dictionary<string, object?>>(documents.Count);
        foreach (var document in documents)
        {
            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in document.Properties)
            {
                properties[key] = value;
            }

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = document.Id,
                ["nodeId"] = document.Id,
                ["parentId"] = document.ParentId,
                ["name"] = document.Name,
                ["type"] = document.Type.ToString(),
                ["kind"] = document.Type.ToString().ToLowerInvariant(),
                ["size"] = document.Size,
                ["sizeBytes"] = document.Size,
                ["childrenCount"] = document.ChildrenCount,
                ["contentsState"] = document.ContentsState.ToString(),
                ["status"] = document.Status,
                ["properties"] = properties
            };

            if (properties.TryGetValue("Path", out var path) && path?.ToString() is { Length: > 0 } pathText)
            {
                row["path"] = pathText;
            }

            rows.Add(row);
        }

        return rows;
    }

    public static List<FileFormatDataLossEntry> BuildEntries(IReadOnlyList<CatalogDocument> documents)
    {
        var entries = new List<FileFormatDataLossEntry>(documents.Count);
        foreach (var document in documents)
        {
            var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in document.Properties)
            {
                if (value is not null)
                {
                    properties[key] = value.ToString();
                }
            }

            entries.Add(new FileFormatDataLossEntry(
                document.Id,
                document.ParentId,
                document.Name,
                document.Type.ToString(),
                document.Size,
                document.Status,
                properties));
        }

        return entries;
    }
}
