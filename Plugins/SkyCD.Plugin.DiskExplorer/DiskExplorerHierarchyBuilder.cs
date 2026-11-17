using System;
using System.Collections.Generic;
using System.Globalization;

namespace SkyCD.Plugin.DiskExplorer;

/// <summary>
/// Reconstructs hierarchical catalog entries from parsed Disk Explorer report items.
/// </summary>
public static class DiskExplorerHierarchyBuilder
{
    private const string SchemaVersionProperty = "DiskExplorerSchemaVersion";
    private const string SourceProperty = "DiskExplorerSource";
    private const string ExportItemTypeProperty = "ExportItemType";

    private sealed record ItemNodeInfo(string Id, string VolumeKey, string FolderPath);

    public static DiskExplorerReportDocument Compose(DiskExplorerParseResult parseResult)
    {
        var summary = new DiskExplorerImportSummary { Title = parseResult.Title };
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

            var parentInfo = item.ParentIndex >= 0 && nodeInfo.TryGetValue(item.ParentIndex, out var resolved)
                ? resolved
                : null;
            var name = ResolveEntryName(item, index);

            switch (item.Kind)
            {
                case DiskExplorerElementKind.Volume:
                    {
                        var volumeId = $"volume:{name}";
                        if (!nodesById.TryGetValue(volumeId, out _))
                        {
                            var mediaNode = CreateEntryNode(volumeId, parentInfo?.Id, "media", name, item, parseResult);
                            nodesById[volumeId] = mediaNode;
                            nodes.Add(mediaNode);
                            summary.Volumes++;
                        }

                        nodeInfo[index] = new ItemNodeInfo(volumeId, name, string.Empty);
                        break;
                    }

                case DiskExplorerElementKind.CategoryFolder:
                case DiskExplorerElementKind.Folder:
                    {
                        var (volumeKey, parentId) = ResolveAncestry(item, parentInfo, nodesById, nodes, parseResult, summary);
                        var relativePath = string.IsNullOrEmpty(parentInfo?.FolderPath)
                            ? name
                            : $"{parentInfo.FolderPath}/{name}";
                        var folderId = $"folder:{volumeKey}/{relativePath}";

                        if (!nodesById.ContainsKey(folderId))
                        {
                            var folderNode = CreateEntryNode(folderId, parentId, "folder", name, item, parseResult);
                            nodesById[folderId] = folderNode;
                            nodes.Add(folderNode);
                            summary.Folders++;
                        }

                        nodeInfo[index] = new ItemNodeInfo(folderId, volumeKey, relativePath);
                        break;
                    }

                default:
                    {
                        var (volumeKey, parentId) = ResolveAncestry(item, parentInfo, nodesById, nodes, parseResult, summary);
                        var fileId = !string.IsNullOrEmpty(parentInfo?.FolderPath)
                            ? $"file:{volumeKey}/{parentInfo.FolderPath}/{name}"
                            : $"file:{volumeKey}/{name}";

                        if (!nodesById.ContainsKey(fileId))
                        {
                            var fileNode = CreateEntryNode(fileId, parentId, "file", name, item, parseResult);
                            nodesById[fileId] = fileNode;
                            nodes.Add(fileNode);
                            summary.Files++;
                        }

                        nodeInfo[index] = new ItemNodeInfo(fileId, volumeKey, parentInfo?.FolderPath ?? string.Empty);
                        break;
                    }
            }
        }

        summary.Notes.AddRange(parseResult.SkippedReasons);
        summary.SkippedRows = parseResult.SkippedItemsCount;
        return new DiskExplorerReportDocument(nodes, summary);
    }

    private static (string VolumeKey, string? ParentId) ResolveAncestry(
        DiskExplorerItem item,
        ItemNodeInfo? parentInfo,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        List<Dictionary<string, object?>> nodes,
        DiskExplorerParseResult parseResult,
        DiskExplorerImportSummary summary)
    {
        if (parentInfo is not null)
        {
            return (parentInfo.VolumeKey, parentInfo.Id);
        }

        const string volumeKey = "Disk Explorer Report";
        var volumeId = $"volume:{volumeKey}";
        if (!nodesById.TryGetValue(volumeId, out _))
        {
            var mediaNode = CreateEntryNode(volumeId, null, "media", volumeKey, item, parseResult);
            nodesById[volumeId] = mediaNode;
            nodes.Add(mediaNode);
            summary.Volumes++;
            summary.Notes.Add("Entries without a volume were attached to a synthesized volume.");
        }

        return (volumeKey, volumeId);
    }

    private static string ResolveEntryName(DiskExplorerItem item, int index)
    {
        if (item.Fields.TryGetValue(DiskExplorerField.Name, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return $"Unnamed {index}";
    }

    private static Dictionary<string, object?> CreateEntryNode(
        string id,
        string? parentId,
        string type,
        string name,
        DiskExplorerItem item,
        DiskExplorerParseResult parseResult)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [SchemaVersionProperty] = "1.0",
            [SourceProperty] = "DiskExplorer"
        };

        if (!string.IsNullOrWhiteSpace(item.RawItemType))
        {
            properties[ExportItemTypeProperty] = item.RawItemType;
        }

        if (item.Fields.TryGetValue(DiskExplorerField.Description, out var description) && !string.IsNullOrWhiteSpace(description))
        {
            properties["Description"] = description;
        }

        if (item.Fields.TryGetValue(DiskExplorerField.Type, out var entryType) && !string.IsNullOrWhiteSpace(entryType))
        {
            properties["FormatType"] = entryType;
        }

        CopyDate(properties, item, DiskExplorerField.Date, "Date");
        CopyDate(properties, item, DiskExplorerField.Time, "Time");

        if (item.HasThumbnail)
        {
            properties["HasThumbnail"] = "true";
        }

        foreach (var (key, value) in item.ExtraFields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[key] = value;
            }
        }

        var size = ReadUnsigned(item, DiskExplorerField.Size);
        long childrenCount = 0;
        if (item.Kind is DiskExplorerElementKind.Volume or DiskExplorerElementKind.CategoryFolder or DiskExplorerElementKind.Folder)
        {
            childrenCount = ReadUnsigned(item, DiskExplorerField.Files) + ReadUnsigned(item, DiskExplorerField.Folders);
            if (item.Fields.TryGetValue(DiskExplorerField.Files, out var files) && !string.IsNullOrWhiteSpace(files))
            {
                properties["FilesCount"] = files.Trim();
            }

            if (item.Fields.TryGetValue(DiskExplorerField.Folders, out var folders) && !string.IsNullOrWhiteSpace(folders))
            {
                properties["FoldersCount"] = folders.Trim();
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["name"] = name,
            ["parentId"] = parentId,
            ["type"] = type,
            ["size"] = size,
            ["childrenCount"] = childrenCount,
            ["properties"] = properties
        };
    }

    private static void CopyDate(
        Dictionary<string, object?> properties,
        DiskExplorerItem item,
        DiskExplorerField field,
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

    private static string? NormalizeDateTime(string value, DiskExplorerField field)
    {
        var trimmed = value.Trim();
        string[] dateFormats =
        [
            "o", "yyyy-MM-dd", "MM/dd/yyyy", "dd.MM.yyyy", "dd/MM/yyyy", "yyyyMMdd",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
        ];
        string[] timeFormats = ["o", "HH:mm:ss", "HH:mm", "hh:mm:ss tt", "hh:mm tt"];

        if (field == DiskExplorerField.Date
            && DateTime.TryParseExact(trimmed, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (field == DiskExplorerField.Time
            && DateTime.TryParseExact(trimmed, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static long ReadUnsigned(DiskExplorerItem item, DiskExplorerField field)
    {
        if (!item.Fields.TryGetValue(field, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        return ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? (long)Math.Min(value, (ulong)long.MaxValue)
            : 0L;
    }
}
