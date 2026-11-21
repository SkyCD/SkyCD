using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkyCD.Plugin.Vvv.Models;

namespace SkyCD.Plugin.Vvv.Import;

/// <summary>
/// Maps parsed VVV entries onto catalog rows consumed by the normal catalog
/// loader: one media node per physical volume (or one for a virtual export),
/// synthesized folder nodes from exported paths, and one file row per entry.
/// Node ids embed the volume and path, so repeated filenames on separate
/// volumes stay distinct and re-imports map stably.
/// </summary>
public static class VvvHierarchyBuilder
{
    public static VvvReportDocument Compose(VvvParseResult parseResult, string exportName)
    {
        var rows = new List<Dictionary<string, object?>>();
        var usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var folderNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var volumes = 0;
        var folders = 0;
        var files = 0;

        string RootIdFor(string volume) =>
            volume.Length == 0 ? "vvv:virtual" : "vvv:volume:" + Sanitize(volume);

        var mediaIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in parseResult.Entries)
        {
            var volume = parseResult.Scope == VvvExportScope.Physical ? entry.Volume : string.Empty;
            if (!mediaIds.TryGetValue(volume, out var mediaId))
            {
                mediaId = MakeUniqueNodeId(RootIdFor(volume), usedNodeIds);
                mediaIds[volume] = mediaId;
                volumes++;

                var mediaName = parseResult.Scope == VvvExportScope.Physical
                    ? volume
                    : string.IsNullOrWhiteSpace(exportName) ? "VVV Virtual Export" : exportName;
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = mediaId,
                    ["parentId"] = null,
                    ["kind"] = "media",
                    ["name"] = mediaName,
                    ["sizeBytes"] = 0L,
                    ["properties"] = VvvMediaProperties(parseResult)
                });
            }

            var parentId = EnsureFolders(rows, mediaId, volume, entry.Path, folderNodeIds, usedNodeIds, ref folders);

            var fileNodeId = MakeUniqueNodeId(
                $"{mediaId}:{(entry.Path.Length == 0 ? string.Empty : Sanitize(entry.Path) + ":")}{Sanitize(entry.Name)}",
                usedNodeIds);
            files++;
            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = fileNodeId,
                ["parentId"] = parentId,
                ["kind"] = "file",
                ["name"] = entry.Name,
                ["sizeBytes"] = entry.Size,
                ["properties"] = VvvFileProperties(parseResult.Scope, volume, entry)
            });
        }

        var summary = new VvvImportSummary
        {
            Scope = parseResult.Scope,
            Delimiter = parseResult.Delimiter,
            HasHeader = parseResult.HasHeader,
            Volumes = volumes,
            Folders = folders,
            Files = files,
            SkippedRows = parseResult.SkippedCount
        };
        foreach (var warning in parseResult.Warnings)
        {
            summary.Notes.Add(warning);
        }

        foreach (var reason in parseResult.SkippedReasons)
        {
            summary.Notes.Add(reason);
        }

        return new VvvReportDocument(rows, summary);
    }

    private static Dictionary<string, object?> VvvMediaProperties(VvvParseResult parseResult)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["VvvScope"] = parseResult.Scope.ToString(),
            ["VvvDelimiter"] = parseResult.Delimiter.ToString(CultureInfo.InvariantCulture),
            ["VvvHasHeader"] = parseResult.HasHeader ? "true" : "false"
        };

        if (parseResult.Scope == VvvExportScope.Virtual)
        {
            properties["Comment"] = "Virtual-folder export: virtual folder structure is not preserved; files are grouped by physical origin path.";
        }

        return properties;
    }

    private static Dictionary<string, object?> VvvFileProperties(VvvExportScope scope, string volume, VvvFileEntry entry)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(volume))
        {
            properties["VvvVolume"] = volume;
        }

        if (!string.IsNullOrWhiteSpace(entry.Extension))
        {
            properties["Extension"] = entry.Extension;
        }

        if (!string.IsNullOrWhiteSpace(entry.LastModified))
        {
            properties["LastModified"] = entry.LastModified;
        }

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            properties["Description"] = entry.Description;
        }

        if (!string.IsNullOrWhiteSpace(entry.PhysicalPath))
        {
            properties["PhysicalPath"] = entry.PhysicalPath;
        }

        properties["VvvScope"] = scope.ToString();
        return properties;
    }

    private static string EnsureFolders(
        List<Dictionary<string, object?>> rows,
        string mediaId,
        string volume,
        string relativePath,
        Dictionary<string, string> folderNodeIds,
        HashSet<string> usedNodeIds,
        ref int folders)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return mediaId;
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parentId = mediaId;
        var accumulated = new StringBuilder();
        foreach (var segment in segments)
        {
            if (accumulated.Length > 0)
            {
                accumulated.Append('/');
            }

            accumulated.Append(segment);
            var key = $"{mediaId}/{accumulated}";
            if (!folderNodeIds.TryGetValue(key, out var folderId))
            {
                folderId = MakeUniqueNodeId($"{mediaId}:{Sanitize(accumulated.ToString())}", usedNodeIds);
                folderNodeIds[key] = folderId;
                folders++;
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = folderId,
                    ["parentId"] = parentId,
                    ["kind"] = "folder",
                    ["name"] = segment,
                    ["sizeBytes"] = 0L,
                    ["properties"] = VvvFolderProperties(volume)
                });
            }

            parentId = folderId;
        }

        return parentId;
    }

    private static Dictionary<string, object?> VvvFolderProperties(string volume)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(volume))
        {
            properties["VvvVolume"] = volume;
        }

        return properties;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }

        return builder.Length == 0 ? "unnamed" : builder.ToString();
    }

    private static string MakeUniqueNodeId(string candidate, HashSet<string> usedNodeIds)
    {
        var attempt = candidate;
        var suffix = 2;
        while (!usedNodeIds.Add(attempt))
        {
            attempt = $"{candidate}-{suffix}";
            suffix++;
        }

        return attempt;
    }
}
