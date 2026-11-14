using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Katalog.CsvIdx.Exceptions;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// Reconstructs SkyCD catalog document rows from parsed Katalog data models:
/// devices, storage profiles, tags, folders.idx and catalog .idx file lists.
/// </summary>
public static class KatalogHierarchyBuilder
{
    public static KatalogCsvIdxReportDocument Build(
        string collectionRoot,
        Dictionary<string, string> parameters,
        List<KatalogDeviceRecord> devices,
        List<KatalogStorageRecord> storages,
        List<KatalogTagRecord> tags,
        List<KatalogIdxCatalog> catalogs,
        Dictionary<string, List<string>> catalogFolders)
    {
        var diagnostics = new List<string>();
        var unsupportedFeatures = new List<string>
        {
            "Thumbnails and extracted preview images",
            "Search history presets and query criteria",
            "Backup mappings and replication schedule links",
            "Hardware serial and storage vendor device properties (mapped to metadata attributes)",
            "Katalog UI filter settings and local database flags"
        };

        var appVersion = parameters.TryGetValue("collection:version", out var v) ? v : "3.0";
        if (string.IsNullOrWhiteSpace(appVersion) && catalogs.Count > 0)
        {
            appVersion = catalogs[0].AppVersion;
        }

        var storageById = storages.ToDictionary(s => s.StorageId);
        var nodesById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var totalFoldersCount = 0;
        var totalFilesCount = 0;
        long totalSizeBytes = 0;
        var brokenReferencesCount = 0;

        // Group tags by item path for quick lookup
        var tagsByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Path) || string.IsNullOrWhiteSpace(tag.Name)) continue;
            var normalizedTagPath = NormalizeItemPath(tag.Path);
            if (!tagsByPath.TryGetValue(normalizedTagPath, out var tagList))
            {
                tagList = [];
                tagsByPath[normalizedTagPath] = tagList;
            }
            tagList.Add(tag.Name.Trim());
        }

        // Map devices and groups
        var deviceById = devices.ToDictionary(d => d.DeviceId);

        // Track created SkyCD node IDs for devices
        var deviceNodeIdMap = new Dictionary<int, string>();

        // Sort devices so parents are created before children
        var sortedDevices = TopologicalSortDevices(devices);

        foreach (var dev in sortedDevices)
        {
            var isVirtualGroup = !string.Equals(dev.Type, "Catalog", StringComparison.OrdinalIgnoreCase);
            var nodeId = $"device:{dev.DeviceId}";
            deviceNodeIdMap[dev.DeviceId] = nodeId;

            string? parentNodeId = null;
            if (dev.ParentId > 0 && deviceNodeIdMap.TryGetValue(dev.ParentId, out var pId))
            {
                parentNodeId = pId;
            }
            else if (dev.ParentId > 0)
            {
                brokenReferencesCount++;
                diagnostics.Add($"Device '{dev.Name}' (ID {dev.DeviceId}) references missing parent ID {dev.ParentId}. Placed at root.");
            }

            var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(dev.Comment)) props["Comment"] = dev.Comment;
            if (!string.IsNullOrWhiteSpace(dev.Path)) props["Path"] = dev.Path;
            if (dev.ExternalId > 0 && storageById.TryGetValue(dev.ExternalId, out var st))
            {
                if (!string.IsNullOrWhiteSpace(st.Label)) props["VolumeLabel"] = st.Label;
                if (!string.IsNullOrWhiteSpace(st.Brand)) props["Brand"] = st.Brand;
                if (!string.IsNullOrWhiteSpace(st.Model)) props["Model"] = st.Model;
                if (!string.IsNullOrWhiteSpace(st.SerialNumber)) props["SerialNumber"] = st.SerialNumber;
                if (!string.IsNullOrWhiteSpace(st.FileSystem)) props["FileSystem"] = st.FileSystem;
                if (!string.IsNullOrWhiteSpace(st.Comment)) props["StorageComment"] = st.Comment;
            }

            nodesById[nodeId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = nodeId,
                ["parentId"] = parentNodeId,
                ["kind"] = isVirtualGroup ? "folder" : "media",
                ["name"] = string.IsNullOrWhiteSpace(dev.Name) ? $"Device_{dev.DeviceId}" : dev.Name,
                ["sizeBytes"] = dev.TotalFileSize.ToString(CultureInfo.InvariantCulture),
                ["properties"] = props
            };
        }

        // If no devices were found in device.csv (e.g., bare collection or missing device.csv),
        // we synthesize media nodes for catalogs directly
        var catalogToDevice = new Dictionary<string, KatalogDeviceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var dev in devices.Where(d => string.Equals(d.Type, "Catalog", StringComparison.OrdinalIgnoreCase)))
        {
            catalogToDevice[dev.Name] = dev;
        }

        // Process catalogs
        foreach (var cat in catalogs)
        {
            var catName = string.IsNullOrWhiteSpace(cat.CatalogName) ? $"Catalog_{cat.CatalogId}" : cat.CatalogName;
            string mediaNodeId;

            if (catalogToDevice.TryGetValue(catName, out var matchedDev))
            {
                mediaNodeId = deviceNodeIdMap[matchedDev.DeviceId];
            }
            else
            {
                // Standalone catalog without device.csv row
                mediaNodeId = $"media:{catName}";
                if (!nodesById.ContainsKey(mediaNodeId))
                {
                    var catProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(cat.SourcePath)) catProps["SourcePath"] = cat.SourcePath;

                    nodesById[mediaNodeId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["nodeId"] = mediaNodeId,
                        ["parentId"] = null,
                        ["kind"] = "media",
                        ["name"] = catName,
                        ["sizeBytes"] = cat.TotalFileSize.ToString(CultureInfo.InvariantCulture),
                        ["properties"] = catProps
                    };
                }
            }

            // Determine explicit folders from folders.idx
            catalogFolders.TryGetValue(catName, out var folderPaths);
            folderPaths ??= [];

            // Add folders from folders.idx
            var folderIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rootPathPrefix = NormalizePathPrefix(cat.SourcePath);

            foreach (var fPath in folderPaths)
            {
                ValidateRelativePath(fPath, collectionRoot);
                EnsureFolderHierarchy(fPath, mediaNodeId, rootPathPrefix, nodesById, folderIdMap, ref totalFoldersCount);
            }

            // Add files from .idx
            foreach (var file in cat.Files)
            {
                ValidateRelativePath(file.FullPath, collectionRoot);

                var normalizedFile = NormalizeItemPath(file.FullPath);
                var folderPart = Path.GetDirectoryName(normalizedFile)?.Replace('\\', '/') ?? string.Empty;
                var fileName = Path.GetFileName(normalizedFile);

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = file.FullPath;
                }

                // Ensure parent folder exists
                var parentId = mediaNodeId;
                if (!string.IsNullOrWhiteSpace(folderPart))
                {
                    parentId = EnsureFolderHierarchy(folderPart, mediaNodeId, rootPathPrefix, nodesById, folderIdMap, ref totalFoldersCount);
                }

                var fileId = $"file:{mediaNodeId}:{normalizedFile}";
                var fileProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(file.DateUpdated)) fileProps["Modified"] = file.DateUpdated;
                if (!string.IsNullOrWhiteSpace(file.ChecksumSha256)) fileProps["SHA256"] = file.ChecksumSha256;
                if (!string.IsNullOrWhiteSpace(file.MimeType)) fileProps["MimeType"] = file.MimeType;
                if (!string.IsNullOrWhiteSpace(file.FileType)) fileProps["FileType"] = file.FileType;

                if (tagsByPath.TryGetValue(normalizedFile, out var itemTags) && itemTags.Count > 0)
                {
                    fileProps["Tags"] = string.Join(", ", itemTags);
                }

                // Media metadata
                if (file.ImageWidth.HasValue && file.ImageHeight.HasValue)
                {
                    fileProps["Dimensions"] = $"{file.ImageWidth.Value}x{file.ImageHeight.Value}";
                }
                if (file.VideoDurationSeconds.HasValue)
                {
                    fileProps["VideoDuration"] = file.VideoDurationSeconds.Value.ToString(CultureInfo.InvariantCulture);
                }
                if (file.AudioDurationSeconds.HasValue)
                {
                    fileProps["AudioDuration"] = file.AudioDurationSeconds.Value.ToString(CultureInfo.InvariantCulture);
                }
                if (!string.IsNullOrWhiteSpace(file.AudioArtist)) fileProps["Artist"] = file.AudioArtist;
                if (!string.IsNullOrWhiteSpace(file.AudioAlbum)) fileProps["Album"] = file.AudioAlbum;
                if (!string.IsNullOrWhiteSpace(file.AudioTitle)) fileProps["Title"] = file.AudioTitle;

                nodesById[fileId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = fileId,
                    ["parentId"] = parentId,
                    ["kind"] = "file",
                    ["name"] = fileName,
                    ["sizeBytes"] = file.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    ["properties"] = fileProps
                };

                totalFilesCount++;
                totalSizeBytes += file.SizeBytes;
            }
        }

        var summary = new KatalogCsvIdxImportSummary(
            AppVersion: appVersion,
            TotalDevices: devices.Count,
            TotalCatalogs: catalogs.Count,
            TotalFolders: totalFoldersCount,
            TotalFiles: totalFilesCount,
            TotalSizeBytes: totalSizeBytes,
            BrokenReferencesCount: brokenReferencesCount,
            CorruptedRecordsCount: 0,
            Diagnostics: diagnostics,
            UnsupportedFeatures: unsupportedFeatures);

        return new KatalogCsvIdxReportDocument(nodesById.Values.ToList(), summary);
    }

    private static string EnsureFolderHierarchy(
        string rawFolderPath,
        string rootMediaId,
        string rootPrefix,
        Dictionary<string, Dictionary<string, object?>> nodesById,
        Dictionary<string, string> folderIdMap,
        ref int totalFoldersCount)
    {
        var normalized = NormalizeItemPath(rawFolderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return rootMediaId;
        }

        if (folderIdMap.TryGetValue(normalized, out var existingId))
        {
            return existingId;
        }

        // Strip source root prefix if the path starts with it
        var relative = normalized;
        if (!string.IsNullOrEmpty(rootPrefix) && relative.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[rootPrefix.Length..].TrimStart('/');
        }

        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentParent = rootMediaId;
        var accumulatedPath = "";

        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            accumulatedPath = string.IsNullOrEmpty(accumulatedPath) ? seg : $"{accumulatedPath}/{seg}";
            var folderId = $"folder:{rootMediaId}:{accumulatedPath}";

            if (!nodesById.ContainsKey(folderId))
            {
                totalFoldersCount++;
                nodesById[folderId] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = folderId,
                    ["parentId"] = currentParent,
                    ["kind"] = "folder",
                    ["name"] = seg,
                    ["sizeBytes"] = "0",
                    ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                };
            }

            currentParent = folderId;
        }

        folderIdMap[normalized] = currentParent;
        return currentParent;
    }

    private static void ValidateRelativePath(string path, string collectionRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // Check for directory traversal sequences
        if (path.Contains("../") || path.Contains(@"..\") || path.StartsWith("..", StringComparison.Ordinal))
        {
            throw new KatalogPathTraversalException(path);
        }
    }

    private static string NormalizeItemPath(string path)
    {
        return path.Replace('\\', '/').Trim().TrimEnd('/');
    }

    private static string NormalizePathPrefix(string path)
    {
        var n = NormalizeItemPath(path);
        return string.IsNullOrEmpty(n) ? "" : n + "/";
    }

    private static List<KatalogDeviceRecord> TopologicalSortDevices(List<KatalogDeviceRecord> devices)
    {
        var result = new List<KatalogDeviceRecord>();
        var visited = new HashSet<int>();
        var byId = devices.ToDictionary(d => d.DeviceId);

        void Visit(KatalogDeviceRecord dev)
        {
            if (visited.Contains(dev.DeviceId)) return;
            visited.Add(dev.DeviceId);

            if (dev.ParentId > 0 && byId.TryGetValue(dev.ParentId, out var parent))
            {
                Visit(parent);
            }

            result.Add(dev);
        }

        foreach (var dev in devices)
        {
            Visit(dev);
        }

        return result;
    }
}
