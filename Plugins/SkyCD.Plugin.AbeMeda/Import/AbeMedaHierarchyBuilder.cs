using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.AbeMeda.Models;

namespace SkyCD.Plugin.AbeMeda.Import;

/// <summary>
/// Reconstructs hierarchical catalog entries from parsed abeMeda XML export items.
/// Supports nested exports (volume/folder/file elements) and flat find-result exports
/// (items carrying catalog/file paths), deriving parents from nesting or paths.
/// </summary>
public static class AbeMedaHierarchyBuilder
{
    private const string NestedImportMode = "Nested";
    private const string FlatImportMode = "Flat";
    private const string SchemaVersionProperty = "AbeMedaSchemaVersion";
    private const string SourceProperty = "AbeMedaSource";

    private sealed record ItemNodeInfo(string Id, string VolumeKey, string FolderPath);

    public static AbeMedaReportDocument Compose(AbeMedaParseResult parseResult)
    {
        var hasPathFields = parseResult.Items.Any(item =>
            item is not null && item.Fields.ContainsKey(AbeMedaField.Path));
        var hasNestedAncestors = parseResult.Items.Any(item => item is not null && item.ParentIndex >= 0);

        if (!hasNestedAncestors && hasPathFields)
        {
            return ComposeFlat(parseResult);
        }

        return ComposeNested(parseResult);
    }

    private static AbeMedaReportDocument ComposeNested(AbeMedaParseResult parseResult)
    {
        var summary = CreateSummary(parseResult, NestedImportMode);
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
                case AbeMedaElementKind.Volume:
                    {
                        var volumeName = ResolveVolumeName(item, name);
                        var volumeId = $"volume:{volumeName}";
                        if (!nodesById.TryGetValue(volumeId, out _))
                        {
                            var mediaNode = CreateEntryNode(volumeId, parentInfo?.Id, "media", volumeName, item, parseResult);
                            nodesById[volumeId] = mediaNode;
                            nodes.Add(mediaNode);
                            summary.Volumes++;
                        }

                        nodeInfo[index] = new ItemNodeInfo(volumeId, volumeName, string.Empty);
                        break;
                    }

                case AbeMedaElementKind.Folder:
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
                        }

                        nodeInfo[index] = new ItemNodeInfo(folderId, volumeKey, relativePath);
                        summary.Folders++;
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
                        }

                        nodeInfo[index] = new ItemNodeInfo(fileId, volumeKey, parentInfo?.FolderPath ?? string.Empty);
                        summary.Files++;
                        break;
                    }
            }
        }

        summary.Notes.AddRange(parseResult.SkippedReasons);
        summary.SkippedRows = parseResult.SkippedItemsCount;
        return new AbeMedaReportDocument(nodes, summary);
    }

    private static AbeMedaReportDocument ComposeFlat(AbeMedaParseResult parseResult)
    {
        var summary = CreateSummary(parseResult, FlatImportMode);
        var nodes = new List<Dictionary<string, object?>>();
        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);

        foreach (var item in parseResult.Items)
        {
            if (item is null)
            {
                continue;
            }

            var path = item.Fields.TryGetValue(AbeMedaField.Path, out var rawPath) ? rawPath : string.Empty;
            var name = ResolveEntryName(item, nodes.Count);
            var parts = SplitPath(path, name);
            if (parts.Length == 0)
            {
                summary.SkippedRows++;
                summary.Notes.Add("Entry has no usable path or name and was skipped.");
                continue;
            }

            var volumeKey = parts[0];
            var volumeId = $"volume:{volumeKey}";
            if (!nodesById.TryGetValue(volumeId, out var volumeNode))
            {
                volumeNode = CreateEntryNode(volumeId, null, "media", volumeKey, item, parseResult);
                nodesById[volumeId] = volumeNode;
                nodes.Add(volumeNode);
                summary.Volumes++;
            }

            var parentId = volumeId;
            var relativeParts = new List<string>();
            for (var i = 1; i < parts.Length - 1; i++)
            {
                relativeParts.Add(parts[i]);
                var folderId = $"folder:{volumeKey}/{string.Join("/", relativeParts)}";
                if (!nodesById.TryGetValue(folderId, out _))
                {
                    var folderNode = CreateEntryNode(folderId, parentId, "folder", parts[i], item, parseResult);
                    nodesById[folderId] = folderNode;
                    nodes.Add(folderNode);
                    summary.Folders++;
                }

                parentId = folderId;
            }

            var leafName = parts[^1];
            var isFolder = item.Kind == AbeMedaElementKind.Folder && item.HasItemChildren;
            var leafId = isFolder
                ? $"folder:{volumeKey}/{string.Join("/", parts.Skip(1))}"
                : $"file:{volumeKey}/{string.Join("/", parts.Skip(1))}";
            if (!nodesById.ContainsKey(leafId))
            {
                var leafNode = CreateEntryNode(leafId, parentId, isFolder ? "folder" : "file", leafName, item, parseResult);
                nodesById[leafId] = leafNode;
                nodes.Add(leafNode);
                if (isFolder)
                {
                    summary.Folders++;
                }
                else
                {
                    summary.Files++;
                }
            }
        }

        summary.Notes.AddRange(parseResult.SkippedReasons);
        summary.SkippedRows += parseResult.SkippedItemsCount;
        return new AbeMedaReportDocument(nodes, summary);
    }

    private static (string VolumeKey, string? ParentId) ResolveAncestry(
        AbeMedaItem item,
        ItemNodeInfo? parentInfo,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        List<Dictionary<string, object?>> nodes,
        AbeMedaParseResult parseResult,
        AbeMedaImportSummary summary)
    {
        if (parentInfo is not null)
        {
            return (parentInfo.VolumeKey, parentInfo.Id);
        }

        const string volumeKey = "AbeMeda Report";
        var volumeId = $"volume:{volumeKey}";
        if (!nodesById.TryGetValue(volumeId, out _))
        {
            var mediaNode = CreateEntryNode(volumeId, null, "media", volumeKey, item, parseResult);
            nodesById[volumeId] = mediaNode;
            nodes.Add(mediaNode);
            summary.Volumes++;
            summary.Notes.Add("Entries without a volume were attached to a synthesized 'AbeMeda Report' volume.");
        }

        return (volumeKey, volumeId);
    }

    private static string[] SplitPath(string path, string fallbackName)
    {
        var combined = string.IsNullOrWhiteSpace(path) ? fallbackName : path;
        return combined.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ResolveEntryName(AbeMedaItem item, int index)
    {
        if (item.Fields.TryGetValue(AbeMedaField.Name, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return $"Unnamed {index}";
    }

    private static string ResolveVolumeName(AbeMedaItem item, string fallback)
    {
        if (item.Fields.TryGetValue(AbeMedaField.Name, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (item.Fields.TryGetValue(AbeMedaField.Path, out var path) && !string.IsNullOrWhiteSpace(path))
        {
            return path.Trim();
        }

        return fallback;
    }

    private static Dictionary<string, object?> CreateEntryNode(
        string id,
        string? parentId,
        string type,
        string name,
        AbeMedaItem item,
        AbeMedaParseResult parseResult)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(parseResult.SchemaVersion))
        {
            properties[SchemaVersionProperty] = parseResult.SchemaVersion;
        }

        properties[SourceProperty] = parseResult.RootElement ?? "AbeMeda";

        if (item.Fields.TryGetValue(AbeMedaField.Comment, out var comment) && !string.IsNullOrWhiteSpace(comment))
        {
            properties["Comment"] = comment;
        }

        if (item.Fields.TryGetValue(AbeMedaField.Label, out var label) && !string.IsNullOrWhiteSpace(label))
        {
            properties["Label"] = label;
        }

        if (item.Fields.TryGetValue(AbeMedaField.Keywords, out var keywords) && !string.IsNullOrWhiteSpace(keywords))
        {
            properties["Keywords"] = keywords;
        }

        if (item.Fields.TryGetValue(AbeMedaField.Kind, out var kind) && !string.IsNullOrWhiteSpace(kind))
        {
            properties["ExportKind"] = kind;
        }

        if (item.Fields.TryGetValue(AbeMedaField.Path, out var path) && !string.IsNullOrWhiteSpace(path))
        {
            properties["OriginalPath"] = path;
        }

        if (item.HasThumbnail)
        {
            properties["HasThumbnail"] = "true";
        }

        CopyDate(properties, item, AbeMedaField.Created, "Created");
        CopyDate(properties, item, AbeMedaField.Modified, "Modified");
        CopyDate(properties, item, AbeMedaField.Accessed, "Accessed");

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

    private static void CopyDate(
        Dictionary<string, object?> properties,
        AbeMedaItem item,
        AbeMedaField field,
        string property)
    {
        if (!item.Fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        properties[property] = NormalizeDate(value);
    }

    private static string NormalizeDate(string value)
    {
        var trimmed = value.Trim();
        string[] formats =
        [
            "o", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
            "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"
        ];
        if (DateTimeOffset.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.ToString("o", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static long ReadSize(AbeMedaItem item)
    {
        if (!item.Fields.TryGetValue(AbeMedaField.Size, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return 0L;
        }

        var trimmed = text.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return Math.Max(0L, integer);
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number))
        {
            return Math.Max(0L, (long)number);
        }

        return 0L;
    }

    private static AbeMedaImportSummary CreateSummary(AbeMedaParseResult parseResult, string mode)
    {
        var summary = new AbeMedaImportSummary
        {
            ImportMode = mode,
            SchemaVersion = parseResult.SchemaVersion
        };

        foreach (var unsupported in parseResult.UnsupportedFields)
        {
            summary.Notes.Add($"Unsupported field retained with source key: {unsupported}");
        }

        return summary;
    }
}
