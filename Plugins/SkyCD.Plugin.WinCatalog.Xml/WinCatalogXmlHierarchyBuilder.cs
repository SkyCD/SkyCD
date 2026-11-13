using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.WinCatalog.Csv;
using SkyCD.Plugin.WinCatalog.Xml.Exceptions;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Reconstructs hierarchical catalog entries from parsed WinCatalog XML report items.
/// Supports nested exports (media/folder/file elements) and flat search-result exports
/// (items carrying catalog/file paths), reusing the shared WinCatalog CSV hierarchy logic.
/// </summary>
public static class WinCatalogXmlHierarchyBuilder
{
    private const string NestedImportMode = "Nested";
    private const string FlatImportMode = "Flat";
    private const string SynthesizedMediaName = "WinCatalog Report";
    private const string SchemaVersionProperty = "WinCatalogSchemaVersion";

    /// <summary>Resolved node identity for a parsed item, used to link child entries to parents.</summary>
    private sealed record ItemNodeInfo(string Id, string MediaName, string FolderPath);

    public static WinCatalogXmlReportDocument Compose(WinCatalogXmlParseResult parseResult)
    {
        var hasPathFields = parseResult.Items.Any(item =>
            item is not null &&
            (item.Fields.ContainsKey(WinCatalogCsvColumn.CatalogPath) ||
             item.Fields.ContainsKey(WinCatalogCsvColumn.FilePath)));
        var hasNestedAncestors = parseResult.Items.Any(item => item is not null && item.ParentIndex >= 0);

        if (!hasNestedAncestors && hasPathFields)
        {
            return ComposeFlat(parseResult);
        }

        return ComposeNested(parseResult);
    }

    private static WinCatalogXmlReportDocument ComposeNested(WinCatalogXmlParseResult parseResult)
    {
        var nodes = new List<Dictionary<string, object?>>();
        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var nodeInfo = new Dictionary<int, ItemNodeInfo>();
        var synthesizedMediaIds = new HashSet<string>(StringComparer.Ordinal);
        var skippedReasons = new List<string>(parseResult.SkippedReasons);
        var skippedCount = parseResult.SkippedItemsCount;

        // Items are indexed in document (start-element) order, so parents always precede
        // their children and ancestor info is already resolved when a child is processed.
        for (var index = 0; index < parseResult.Items.Count; index++)
        {
            var item = parseResult.Items[index];
            if (item is null)
            {
                continue;
            }

            var parentInfo = item.ParentIndex >= 0 ? nodeInfo[item.ParentIndex] : null;
            var name = ResolveEntryName(item, index);

            switch (item.Kind)
            {
                case WinCatalogXmlElementKind.Media:
                    {
                        var mediaName = ResolveMediaName(item);
                        var mediaId = $"media:{mediaName}";
                        if (!nodesById.TryGetValue(mediaId, out var mediaNode))
                        {
                            mediaNode = CreateMediaNode(mediaId, parentInfo?.Id, mediaName, item, parseResult);
                            nodesById[mediaId] = mediaNode;
                            nodes.Add(mediaNode);
                        }

                        nodeInfo[index] = new ItemNodeInfo(mediaId, mediaName, string.Empty);
                        break;
                    }

                case WinCatalogXmlElementKind.Folder:
                    {
                        var (mediaName, parentId) = ResolveAncestry(
                            item, parentInfo, synthesizedMediaIds, nodesById, nodes, parseResult);
                        var relativePath = string.IsNullOrEmpty(parentInfo?.FolderPath)
                            ? name
                            : $"{parentInfo.FolderPath}/{name}";
                        var folderId = $"folder:{mediaName}/{relativePath}";

                        if (!nodesById.ContainsKey(folderId))
                        {
                            var folderNode = CreateEntryNode(
                                folderId, parentId, "folder", name, item, parseResult);
                            nodesById[folderId] = folderNode;
                            nodes.Add(folderNode);
                        }

                        nodeInfo[index] = new ItemNodeInfo(folderId, mediaName, relativePath);
                        break;
                    }

                default:
                    {
                        var (mediaName, parentId) = ResolveAncestry(
                            item, parentInfo, synthesizedMediaIds, nodesById, nodes, parseResult);
                        var relativePath = string.IsNullOrEmpty(parentInfo?.FolderPath)
                            ? name
                            : $"{parentInfo.FolderPath}/{name}";
                        var fileId = $"file:{mediaName}/{relativePath}";
                        if (!nodesById.ContainsKey(fileId))
                        {
                            var fileNode = CreateEntryNode(
                                fileId, parentId, "file", name, item, parseResult);
                            nodesById[fileId] = fileNode;
                            nodes.Add(fileNode);
                        }

                        nodeInfo[index] = new ItemNodeInfo(fileId, mediaName, parentInfo?.FolderPath ?? string.Empty);
                        break;
                    }
            }
        }

        foreach (var synthesizedMediaId in synthesizedMediaIds)
        {
            var synthesizedName = synthesizedMediaId.StartsWith("media:", StringComparison.Ordinal)
                ? synthesizedMediaId["media:".Length..]
                : synthesizedMediaId;
            skippedReasons.Add(
                $"Synthesized media root '{synthesizedName}' for items exported without their disk ancestor.");
        }

        var summary = new WinCatalogXmlImportSummary(
            TotalItemsRead: parseResult.Items.Count(item => item is not null) + skippedCount,
            ImportedItemsCount: parseResult.Items.Count(item => item is not null),
            SkippedItemsCount: skippedCount,
            IsPartialReport: synthesizedMediaIds.Count > 0 || skippedCount > 0,
            SkippedReasons: skippedReasons,
            UnsupportedFields: parseResult.UnsupportedFields,
            DetectedRootElement: parseResult.RootElement,
            DetectedSchemaVersion: parseResult.SchemaVersion,
            ImportMode: NestedImportMode);

        return new WinCatalogXmlReportDocument(nodes, summary);
    }

    /// <summary>
    /// Resolves the media name and parent node id for a folder/file entry. When the entry
    /// has no exported media or folder ancestor, a media root is synthesized from the
    /// disk field (or a generic name); synthesized roots mark the report as partial.
    /// </summary>
    private static (string MediaName, string ParentId) ResolveAncestry(
        WinCatalogXmlItem item,
        ItemNodeInfo? parentInfo,
        HashSet<string> synthesizedMediaIds,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        List<Dictionary<string, object?>> nodes,
        WinCatalogXmlParseResult parseResult)
    {
        if (parentInfo is not null)
        {
            return (parentInfo.MediaName, parentInfo.Id);
        }

        var mediaName = item.Fields.TryGetValue(WinCatalogCsvColumn.Disk, out var disk) && !string.IsNullOrWhiteSpace(disk)
            ? disk
            : SynthesizedMediaName;
        var mediaId = $"media:{mediaName}";
        if (!nodesById.ContainsKey(mediaId))
        {
            var mediaNode = CreateMediaNode(mediaId, null, mediaName, item, parseResult);
            nodesById[mediaId] = mediaNode;
            nodes.Add(mediaNode);
            synthesizedMediaIds.Add(mediaId);
        }

        return (mediaName, mediaId);
    }

    private static string ResolveMediaName(WinCatalogXmlItem item)
    {
        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Name, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.VolumeLabel, out var volumeLabel) && !string.IsNullOrWhiteSpace(volumeLabel))
        {
            return volumeLabel;
        }

        return "Media";
    }

    private static string ResolveEntryName(WinCatalogXmlItem item, int index)
    {
        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Filename, out var filename) && !string.IsNullOrWhiteSpace(filename))
        {
            return Unescape(filename);
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Name, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return Unescape(name);
        }

        var path = item.Fields.TryGetValue(WinCatalogCsvColumn.CatalogPath, out var catalogPath)
            ? catalogPath
            : item.Fields.GetValueOrDefault(WinCatalogCsvColumn.FilePath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            var segments = path.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 0)
            {
                return Unescape(segments[^1]);
            }
        }

        return item.Kind == WinCatalogXmlElementKind.Folder ? $"Folder_{index + 1}" : $"Item_{index + 1}";
    }

    private static Dictionary<string, object?> CreateMediaNode(
        string mediaId,
        string? parentId,
        string mediaName,
        WinCatalogXmlItem item,
        WinCatalogXmlParseResult parseResult)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (item.Fields.TryGetValue(WinCatalogCsvColumn.VolumeLabel, out var volumeLabel) && !string.IsNullOrWhiteSpace(volumeLabel))
        {
            properties["VolumeLabel"] = volumeLabel;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.PhysicalLocation, out var location) && !string.IsNullOrWhiteSpace(location))
        {
            properties["PhysicalLocation"] = location;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Comments, out var comments) && !string.IsNullOrWhiteSpace(comments))
        {
            properties["Comments"] = comments;
        }

        AddSchemaVersionProperty(properties, parseResult);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = mediaId,
            ["parentId"] = parentId,
            ["kind"] = "media",
            ["name"] = mediaName,
            ["sizeBytes"] = WinCatalogHierarchyBuilder.ParseSize(item.Fields.GetValueOrDefault(WinCatalogCsvColumn.Size))
                .ToString(CultureInfo.InvariantCulture),
            ["properties"] = properties
        };
    }

    private static Dictionary<string, object?> CreateEntryNode(
        string nodeId,
        string? parentId,
        string kind,
        string name,
        WinCatalogXmlItem item,
        WinCatalogXmlParseResult parseResult)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = kind,
            ["name"] = name,
            ["sizeBytes"] = WinCatalogHierarchyBuilder.ParseSize(item.Fields.GetValueOrDefault(WinCatalogCsvColumn.Size))
                .ToString(CultureInfo.InvariantCulture),
            ["properties"] = BuildProperties(item, parseResult)
        };
    }

    private static Dictionary<string, object?> BuildProperties(WinCatalogXmlItem item, WinCatalogXmlParseResult parseResult)
    {
        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.CatalogPath, out var catalogPath) && !string.IsNullOrWhiteSpace(catalogPath))
        {
            props["WinCatalogPath"] = catalogPath;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.FilePath, out var filePath) && !string.IsNullOrWhiteSpace(filePath))
        {
            props["WinCatalogFilePath"] = filePath;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Created, out var createdText) && !string.IsNullOrWhiteSpace(createdText))
        {
            props["DateCreated"] = WinCatalogHierarchyBuilder.NormalizeDate(createdText);
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Modified, out var modifiedText) && !string.IsNullOrWhiteSpace(modifiedText))
        {
            props["DateModified"] = WinCatalogHierarchyBuilder.NormalizeDate(modifiedText);
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Comments, out var comments) && !string.IsNullOrWhiteSpace(comments))
        {
            props["Comments"] = comments;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Tags, out var tags) && !string.IsNullOrWhiteSpace(tags))
        {
            props["Tags"] = tags;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Rating, out var rating) && !string.IsNullOrWhiteSpace(rating))
        {
            props["Rating"] = rating;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.MD5, out var md5) && !string.IsNullOrWhiteSpace(md5))
        {
            props["MD5"] = md5;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.SHA256, out var sha256) && !string.IsNullOrWhiteSpace(sha256))
        {
            props["SHA256"] = sha256;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.Blake2, out var blake2) && !string.IsNullOrWhiteSpace(blake2))
        {
            props["Blake2"] = blake2;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.CRC32, out var crc32) && !string.IsNullOrWhiteSpace(crc32))
        {
            props["CRC32"] = crc32;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.PhysicalLocation, out var location) && !string.IsNullOrWhiteSpace(location))
        {
            props["PhysicalLocation"] = location;
        }

        if (item.Fields.TryGetValue(WinCatalogCsvColumn.VolumeLabel, out var volumeLabel) && !string.IsNullOrWhiteSpace(volumeLabel))
        {
            props["VolumeLabel"] = volumeLabel;
        }

        if (!string.IsNullOrWhiteSpace(item.IconValue))
        {
            props["Icon"] = item.IconValue;
        }

        AddSchemaVersionProperty(props, parseResult);
        return props;
    }

    private static void AddSchemaVersionProperty(
        Dictionary<string, object?> properties,
        WinCatalogXmlParseResult parseResult)
    {
        if (!string.IsNullOrWhiteSpace(parseResult.SchemaVersion))
        {
            properties[SchemaVersionProperty] = parseResult.SchemaVersion!;
        }
    }

    private static string Unescape(string value)
    {
        return WinCatalogHierarchyBuilder.UnescapeWinCatalogTokens(value);
    }

    /// <summary>
    /// Composes nodes from flat search-result exports by delegating to the shared
    /// WinCatalog CSV hierarchy builder with synthetic report rows.
    /// </summary>
    private static WinCatalogXmlReportDocument ComposeFlat(WinCatalogXmlParseResult parseResult)
    {
        var csvColumns = new[]
        {
            (WinCatalogCsvColumn.Disk, "Disk"),
            (WinCatalogCsvColumn.CatalogPath, "Catalog Path"),
            (WinCatalogCsvColumn.FilePath, "File Path"),
            (WinCatalogCsvColumn.Name, "Name"),
            (WinCatalogCsvColumn.Filename, "Filename"),
            (WinCatalogCsvColumn.Size, "Size"),
            (WinCatalogCsvColumn.Type, "Type"),
            (WinCatalogCsvColumn.Modified, "Modified"),
            (WinCatalogCsvColumn.Created, "Created"),
            (WinCatalogCsvColumn.Comments, "Comments"),
            (WinCatalogCsvColumn.Tags, "Tags"),
            (WinCatalogCsvColumn.Rating, "Rating"),
            (WinCatalogCsvColumn.MD5, "MD5"),
            (WinCatalogCsvColumn.SHA256, "SHA256"),
            (WinCatalogCsvColumn.Blake2, "Blake2"),
            (WinCatalogCsvColumn.CRC32, "CRC32"),
            (WinCatalogCsvColumn.PhysicalLocation, "Physical Location")
        };

        var rawRows = new List<List<string>> { csvColumns.Select(column => column.Item2).ToList() };
        foreach (var item in parseResult.Items)
        {
            if (item is null)
            {
                continue;
            }

            var row = new List<string>();
            foreach (var (column, _) in csvColumns)
            {
                string? value = null;
                if (column == WinCatalogCsvColumn.Type &&
                    !item.Fields.TryGetValue(WinCatalogCsvColumn.Type, out value))
                {
                    value = item.HasItemChildren ? "Folder" : "File";
                }
                else if (item.Fields.TryGetValue(column, out var fieldValue))
                {
                    value = fieldValue;
                }

                row.Add(value ?? string.Empty);
            }

            rawRows.Add(row);
        }

        var csvDocument = WinCatalogHierarchyBuilder.Build(rawRows, ',');

        var skippedReasons = new List<string>(parseResult.SkippedReasons);
        skippedReasons.AddRange(csvDocument.Summary.SkippedReasons);
        var unsupportedFields = new List<string>(parseResult.UnsupportedFields);
        unsupportedFields.AddRange(csvDocument.Summary.UnsupportedFields);

        var summary = new WinCatalogXmlImportSummary(
            TotalItemsRead: parseResult.Items.Count(item => item is not null),
            ImportedItemsCount: csvDocument.Summary.ImportedRecordsCount,
            SkippedItemsCount: parseResult.SkippedItemsCount + csvDocument.Summary.SkippedRecordsCount,
            IsPartialReport: csvDocument.Summary.IsPartialReport || parseResult.SkippedItemsCount > 0,
            SkippedReasons: skippedReasons,
            UnsupportedFields: unsupportedFields,
            DetectedRootElement: parseResult.RootElement,
            DetectedSchemaVersion: parseResult.SchemaVersion,
            ImportMode: FlatImportMode);

        return WinCatalogXmlReportDocument.FromCsv(csvDocument, summary);
    }
}
