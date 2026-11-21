using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Plugin.WhereIsIt.Models;

namespace SkyCD.Plugin.WhereIsIt.Import;

/// <summary>
/// Reconstructs hierarchical catalog entries from parsed Where Is It? items.
/// Volumes group by disk name; folders derive from backslash-separated paths;
/// file names append the extension when present.
/// </summary>
public static class WhereIsItHierarchyBuilder
{
    private const string SchemaVersionProperty = "WhereIsItSchemaVersion";
    private const string SourceProperty = "WhereIsItSource";
    private const string ExportItemTypeProperty = "ExportItemType";

    private sealed record ItemNodeInfo(string Id, string VolumeKey, string FolderPath);

    public static WhereIsItReportDocument Compose(WhereIsItParseResult parseResult)
    {
        var summary = new WhereIsItImportSummary { Title = parseResult.Title };
        foreach (var unsupported in parseResult.UnsupportedFields)
        {
            summary.Notes.Add($"Unsupported field retained with source key: {unsupported}");
        }

        var nodes = new List<Dictionary<string, object?>>();
        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var nodeInfo = new Dictionary<int, ItemNodeInfo>();

        for (var index = 0; index < parseResult.Items.Count; index++)
        {
            var item = parseResult.Items[index];
            if (item is null)
            {
                continue;
            }

            var name = ResolveEntryName(item, index);
            switch (item.Kind)
            {
                case WhereIsItElementKind.File:
                case WhereIsItElementKind.Folder:
                    AppendEntry(item, index, name, parseResult, nodes, nodesById, nodeInfo, summary);
                    break;
            }
        }

        // Deterministic catalog order: volumes, then subtrees in encounter order.
        summary.Notes.AddRange(parseResult.SkippedReasons);
        summary.SkippedRows = parseResult.SkippedItemsCount;

        var childCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var parentId = Convert.ToString(node["parentId"], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                childCounts[parentId] = childCounts.TryGetValue(parentId, out var current) ? current + 1 : 1;
            }
        }

        foreach (var node in nodes)
        {
            var id = Convert.ToString(node["id"], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(id) && childCounts.TryGetValue(id, out var count))
            {
                node["childrenCount"] = (long)count;
            }
        }

        return new WhereIsItReportDocument(nodes, summary);
    }

    private static void AppendEntry(
        WhereIsItItem item,
        int index,
        string name,
        WhereIsItParseResult parseResult,
        List<Dictionary<string, object?>> nodes,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        Dictionary<int, ItemNodeInfo> nodeInfo,
        WhereIsItImportSummary summary)
    {
        var diskName = item.Fields[WhereIsItField.DiskName].Trim();
        var segments = SplitPath(item.Fields[WhereIsItField.Path]);
        var volumeId = $"volume:{diskName}";
        if (!nodesById.TryGetValue(volumeId, out _))
        {
            var mediaNode = CreateEntryNode(volumeId, null, "media", diskName, item, parseResult, isVolume: true);
            nodesById[volumeId] = mediaNode;
            nodes.Add(mediaNode);
            summary.Volumes++;
        }

        var parentId = volumeId;
        var relativeParts = new List<string>();
        foreach (var segment in segments)
        {
            relativeParts.Add(segment);
            var folderId = $"folder:{diskName}/{string.Join("/", relativeParts)}";
            if (!nodesById.TryGetValue(folderId, out _))
            {
                var folderNode = CreateEntryNode(folderId, parentId, "folder", segment, item, parseResult, isVolume: false);
                nodesById[folderId] = folderNode;
                nodes.Add(folderNode);
                summary.Folders++;
            }

            parentId = folderId;
        }

        var relative = string.Join("/", segments);
        var leafId = item.Kind == WhereIsItElementKind.Folder
            ? $"folder:{diskName}/{relative}/{name}".Replace("//", "/", StringComparison.Ordinal)
            : $"file:{diskName}/{relative}/{name}".Replace("//", "/", StringComparison.Ordinal);
        if (nodesById.ContainsKey(leafId))
        {
            summary.SkippedRows++;
            summary.Notes.Add($"Duplicate entry '{name}' under '{relative}' was skipped.");
            return;
        }

        var leafNode = CreateEntryNode(leafId, parentId, item.Kind == WhereIsItElementKind.Folder ? "folder" : "file",
            name, item, parseResult, isVolume: false);
        nodesById[leafId] = leafNode;
        nodes.Add(leafNode);
        nodeInfo[index] = new ItemNodeInfo(leafId, diskName,
            string.IsNullOrEmpty(relative) ? name : $"{relative}/{name}");
        if (item.Kind == WhereIsItElementKind.Folder)
        {
            summary.Folders++;
        }
        else
        {
            summary.Files++;
            if (leafNode["size"] is long size)
            {
                summary.TotalBytes += size;
            }
        }
    }

    private static string[] SplitPath(string path)
    {
        return path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ResolveEntryName(WhereIsItItem item, int index)
    {
        var name = item.Fields[WhereIsItField.Name].Trim();
        if (item.Fields.TryGetValue(WhereIsItField.Extension, out var extension)
            && !string.IsNullOrWhiteSpace(extension))
        {
            return $"{name}.{extension.Trim()}";
        }

        return string.IsNullOrWhiteSpace(name) ? $"Unnamed {index}" : name;
    }

    private static Dictionary<string, object?> CreateEntryNode(
        string id,
        string? parentId,
        string type,
        string name,
        WhereIsItItem item,
        WhereIsItParseResult parseResult,
        bool isVolume)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [SchemaVersionProperty] = "1.0",
            [SourceProperty] = "WhereIsIt"
        };

        if (!string.IsNullOrWhiteSpace(item.RawItemType))
        {
            properties[ExportItemTypeProperty] = item.RawItemType;
        }

        if (item.Fields.TryGetValue(WhereIsItField.Description, out var description)
            && !string.IsNullOrWhiteSpace(description))
        {
            properties["Description"] = description;
        }

        if (item.Fields.TryGetValue(WhereIsItField.Category, out var category)
            && !string.IsNullOrWhiteSpace(category))
        {
            properties["Category"] = category.Trim();
        }

        if (item.Fields.TryGetValue(WhereIsItField.Flag, out var flag)
            && !string.IsNullOrWhiteSpace(flag))
        {
            properties["Flag"] = flag.Trim();
        }

        if (isVolume)
        {
            CopyText(properties, item, WhereIsItField.DiskType, "DiskType");
            CopyText(properties, item, WhereIsItField.DiskNumber, "DiskNumber");
            CopyText(properties, item, WhereIsItField.DiskLocation, "DiskLocation");
        }
        else
        {
            if (item.Fields.TryGetValue(WhereIsItField.Crc, out var crc) && !string.IsNullOrWhiteSpace(crc))
            {
                properties["Crc32"] = crc.Trim();
            }

            if (item.Fields.TryGetValue(WhereIsItField.Path, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                properties["OriginalPath"] = path.Trim();
            }
        }

        CopyDate(properties, item, WhereIsItField.Date, "Date");
        CopyDate(properties, item, WhereIsItField.Time, "Time");

        foreach (var (key, value) in item.ExtraFields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[key] = value;
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["name"] = name,
            ["parentId"] = parentId,
            ["type"] = type,
            ["size"] = ReadSize(item),
            ["childrenCount"] = 0L,
            ["properties"] = properties
        };
    }

    private static void CopyText(
        Dictionary<string, object?> properties,
        WhereIsItItem item,
        WhereIsItField field,
        string property)
    {
        if (item.Fields.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            properties[property] = value.Trim();
        }
    }

    private static void CopyDate(
        Dictionary<string, object?> properties,
        WhereIsItItem item,
        WhereIsItField field,
        string property)
    {
        if (!item.Fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        properties[property] = value.Trim();
        var normalized = NormalizeDateTime(value, field);
        if (normalized is not null)
        {
            properties[property + "Normalized"] = normalized;
        }
    }

    private static string? NormalizeDateTime(string value, WhereIsItField field)
    {
        var trimmed = value.Trim();
        if (field == WhereIsItField.Date)
        {
            string[] dateFormats =
            [
                "o", "yyyy-MM-dd", "MM/dd/yyyy", "dd.MM.yyyy", "dd/MM/yyyy", "yyyyMMdd",
                "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "ddd MMM dd HH:mm:ss yyyy"
            ];
            if (DateTime.TryParseExact(trimmed, dateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lenient))
            {
                return lenient.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return null;
        }

        string[] timeFormats = ["o", "HH:mm:ss", "HH:mm", "hh:mm:ss tt", "hh:mm tt"];
        if (DateTime.TryParseExact(trimmed, timeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
        {
            return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lenientTime))
        {
            return lenientTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static long ReadSize(WhereIsItItem item)
    {
        if (!item.Fields.TryGetValue(WhereIsItField.Size, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return Math.Max(0L, integer);
        }

        return 0L;
    }
}
