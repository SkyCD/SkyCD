using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.WinCatalog.Native.Models;
using SkyCD.Plugin.WinCatalog.Native.Parsing;

namespace SkyCD.Plugin.WinCatalog.Native.Import;

/// <summary>
/// Reconstructs media/folder/file hierarchy from generic native catalog rows.
/// </summary>
public static class WinCatalogNativeHierarchyBuilder
{
    public static WinCatalogNativeReportDocument Build(
        WinCatalogNativeReader.Snapshot snapshot, string fileStem)
    {
        var mediaName = string.IsNullOrWhiteSpace(fileStem) ? "WinCatalog" : fileStem.Trim();
        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var skippedReasons = new List<string>();
        var imported = 0;

        var mediaId = $"media:{mediaName}";
        nodesById[mediaId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = mediaId,
            ["parentId"] = null,
            ["kind"] = "media",
            ["name"] = mediaName,
            ["sizeBytes"] = "0",
            ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["WinCatalogSource"] = "native",
                ["WinCatalogSchema"] = snapshot.SchemaVersion ?? "unknown"
            }
        };

        foreach (var record in snapshot.Records)
        {
            try
            {
                var segments = SplitPath(record.FullPath) ?? SplitPath(record.ParentPath);
                var folderSegments = new List<string>();
                string leafName = record.Name;
                if (segments is { Count: > 0 })
                {
                    if (segments[^1].Equals(record.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        folderSegments = segments.Take(segments.Count - 1).ToList();
                    }
                    else
                    {
                        folderSegments = segments;
                    }
                }

                var parentId = EnsureFolders(nodesById, mediaId, mediaName, folderSegments);
                var kind = record.Kind == "folder" ? "folder" : "file";
                var nodeId = $"{(kind == "folder" ? "folder" : "file")}:{mediaName}/{string.Join("/", folderSegments.Append(leafName))}";
                if (nodesById.ContainsKey(nodeId))
                {
                    nodeId = $"{nodeId}#{imported}";
                }

                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(record.FullPath))
                {
                    properties["WinCatalogPath"] = record.FullPath;
                }

                if (!string.IsNullOrWhiteSpace(record.Comment))
                {
                    properties["Comments"] = record.Comment;
                }

                if (!string.IsNullOrWhiteSpace(record.Tags))
                {
                    properties["Tags"] = record.Tags;
                }

                if (!string.IsNullOrWhiteSpace(record.Created))
                {
                    properties["DateCreated"] = record.Created;
                }

                if (!string.IsNullOrWhiteSpace(record.Modified))
                {
                    properties["DateModified"] = record.Modified;
                }

                properties["WinCatalogTable"] = record.Table;

                nodesById[nodeId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = nodeId,
                    ["parentId"] = parentId,
                    ["kind"] = kind,
                    ["name"] = leafName,
                    ["sizeBytes"] = record.Size.ToString(CultureInfo.InvariantCulture),
                    ["properties"] = properties
                };
                imported++;
            }
            catch (Exception ex)
            {
                skippedReasons.Add($"{record.Table}/{record.Name}: {ex.Message}");
            }
        }

        var summary = new WinCatalogNativeImportSummary(
            TotalRecordsRead: snapshot.Records.Count,
            ImportedRecordsCount: imported,
            SkippedRecordsCount: skippedReasons.Count,
            IsPartialReport: skippedReasons.Count > 0,
            SkippedReasons: skippedReasons,
            UnsupportedFields: ["Thumbnails", "Icons", "SearchHistory", "CatalogSettings"],
            DetectedTables: snapshot.Tables.Select(t => t.Name).ToList(),
            DetectedSchema: snapshot.SchemaVersion ?? "unknown");

        return new WinCatalogNativeReportDocument(nodesById.Values.ToList(), summary);
    }

    private static string EnsureFolders(
        Dictionary<string, Dictionary<string, object?>> nodesById,
        string mediaId,
        string mediaName,
        List<string> folderSegments)
    {
        var currentParentId = mediaId;
        var currentPath = mediaName;
        foreach (var folder in folderSegments)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            currentPath = $"{currentPath}/{folder}";
            var folderId = $"folder:{currentPath}";
            if (!nodesById.ContainsKey(folderId))
            {
                nodesById[folderId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = folderId,
                    ["parentId"] = currentParentId,
                    ["kind"] = "folder",
                    ["name"] = folder,
                    ["sizeBytes"] = "0",
                    ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                };
            }

            currentParentId = folderId;
        }

        return currentParentId;
    }

    private static List<string>? SplitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
