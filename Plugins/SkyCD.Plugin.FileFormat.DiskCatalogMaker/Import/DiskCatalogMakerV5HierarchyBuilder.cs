using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Import;

/// <summary>
/// Maps a parsed v5 catalog to the shared flat node-row contract consumed by the catalog loader.
/// </summary>
public static class DiskCatalogMakerV5HierarchyBuilder
{
    private const int MaximumHierarchyWarnings = 1000;

    /// <summary>
    /// Builds catalog rows for every volume and item. When a package root is supplied,
    /// thumbnail references are checked against the package on a best-effort basis.
    /// </summary>
    public static DiskCatalogMakerV5ReportDocument Build(
        DiskCatalogMakerV5Catalog catalog,
        string? packageRoot = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var warnings = new List<string>(catalog.Warnings);
        var rows = new List<Dictionary<string, object?>>();

        var itemsByVolume = catalog.Items
            .GroupBy(static item => item.VolumeId)
            .ToDictionary(static group => group.Key, static group => group.ToDictionary(static item => item.ItemId));
        var aliasesByItemId = new Dictionary<uint, DiskCatalogMakerV5Alias>();
        foreach (var alias in catalog.Aliases)
        {
            aliasesByItemId.TryAdd(alias.ItemId, alias);
        }

        var thumbnailsByKey = new Dictionary<uint, DiskCatalogMakerV5Thumbnail>();
        foreach (var thumbnail in catalog.Thumbnails)
        {
            thumbnailsByKey.TryAdd(thumbnail.Key, thumbnail);
        }

        var thumbnailExistence = new Dictionary<string, bool>(StringComparer.Ordinal);
        var importedItemCount = 0;

        foreach (var volume in catalog.Volumes)
        {
            rows.Add(BuildVolumeRow(volume, CreateMediaNodeId(volume.VolumeId)));

            if (!itemsByVolume.TryGetValue(volume.VolumeId, out var volumeItems))
            {
                continue;
            }

            foreach (var entry in OrderItems(volumeItems, volume.Name))
            {
                importedItemCount++;
                rows.Add(BuildItemRow(
                    entry.Item,
                    entry.Path,
                    volume,
                    volumeItems,
                    aliasesByItemId,
                    thumbnailsByKey,
                    thumbnailExistence,
                    packageRoot,
                    warnings));
            }
        }

        var knownVolumeIds = catalog.Volumes.Select(static volume => volume.VolumeId).ToHashSet();
        foreach (var orphanGroup in catalog.Items
                     .Where(item => !knownVolumeIds.Contains(item.VolumeId))
                     .GroupBy(static item => item.VolumeId))
        {
            var orphanVolume = new DiskCatalogMakerV5Volume
            {
                VolumeId = orphanGroup.Key,
                Name = $"Volume {orphanGroup.Key}"
            };
            AddWarning(warnings,
                $"items reference volume {orphanGroup.Key}, which has no volume record; a media node was synthesized.");
            rows.Add(BuildVolumeRow(orphanVolume, CreateMediaNodeId(orphanVolume.VolumeId)));

            var orphanItems = orphanGroup.ToDictionary(static item => item.ItemId);
            foreach (var entry in OrderItems(orphanItems, orphanVolume.Name))
            {
                importedItemCount++;
                rows.Add(BuildItemRow(
                    entry.Item,
                    entry.Path,
                    orphanVolume,
                    orphanItems,
                    aliasesByItemId,
                    thumbnailsByKey,
                    thumbnailExistence,
                    packageRoot,
                    warnings));
            }
        }

        var summary = new DiskCatalogMakerV5ImportSummary(
            CatalogRevision: catalog.Revision,
            TableCount: catalog.Tables.Count,
            KnownTableCount: catalog.Tables.Count(static table => table.IsKnown),
            UnknownTableCount: catalog.UnknownTables.Count,
            VolumeCount: catalog.Volumes.Count,
            ItemCount: catalog.Items.Count,
            ImportedItemCount: importedItemCount,
            PackageRoot: packageRoot,
            Warnings: warnings,
            UnknownTables: catalog.UnknownTables);

        return new DiskCatalogMakerV5ReportDocument(rows, summary);
    }

    private static IReadOnlyList<(DiskCatalogMakerV5Item Item, string Path)> OrderItems(
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Item> items,
        string volumeName)
    {
        return items.Values
            .Select(item => (Item: item, Path: BuildPath(item, items, volumeName)))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Item.ItemId)
            .ToList();
    }

    private static Dictionary<string, object?> BuildVolumeRow(DiskCatalogMakerV5Volume volume, string nodeId)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["VolumeId"] = volume.VolumeId.ToString(CultureInfo.InvariantCulture),
            ["Kind"] = DescribeVolumeKind(volume.Kind),
            ["FileSystem"] = DescribeFileSystem(volume.FileSystem),
            ["CapacityBytes"] = volume.CapacityBytes.ToString(CultureInfo.InvariantCulture),
            ["FreeBytes"] = volume.FreeBytes.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrEmpty(volume.Source))
        {
            properties["Source"] = volume.Source;
        }

        if (volume.Created is { } created)
        {
            properties["Created"] = FormatTimestamp(created);
        }

        if (volume.Scanned is { } scanned)
        {
            properties["Scanned"] = FormatTimestamp(scanned);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = null,
            ["kind"] = "media",
            ["name"] = volume.Name,
            ["sizeBytes"] = "0",
            ["properties"] = properties
        };
    }

    private static Dictionary<string, object?> BuildItemRow(
        DiskCatalogMakerV5Item item,
        string path,
        DiskCatalogMakerV5Volume volume,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Item> volumeItems,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Alias> aliasesByItemId,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Thumbnail> thumbnailsByKey,
        Dictionary<string, bool> thumbnailExistence,
        string? packageRoot,
        List<string> warnings)
    {
        var nodeId = CreateItemNodeId(item.VolumeId, item.ItemId);
        var mediaNodeId = CreateMediaNodeId(item.VolumeId);
        var parentId = ResolveParentId(item, mediaNodeId, volumeItems, warnings);

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RecordId"] = item.ItemId.ToString(CultureInfo.InvariantCulture),
            ["CatalogPath"] = path,
            ["Kind"] = DescribeItemKind(item.Kind),
            ["CatalogItemKind"] = item.Kind.ToString(),
            ["Volume"] = volume.Name
        };

        if (item.TypeCode is not null)
        {
            properties["TypeCode"] = item.TypeCode;
        }

        if (item.CreatorCode is not null)
        {
            properties["Creator"] = item.CreatorCode;
        }

        if (!string.IsNullOrEmpty(item.Comment))
        {
            properties["Comments"] = item.Comment;
        }

        if (item.HasLabel)
        {
            properties["Label"] = item.Label.ToString(CultureInfo.InvariantCulture);
            properties["LabelName"] = DescribeLabel(item.Label);
        }

        if (item.PosixMode is { } posixMode)
        {
            properties["PosixMode"] = "0" + Convert.ToString(posixMode, 8);
        }

        if (item.Created is { } created)
        {
            properties["Created"] = FormatTimestamp(created);
        }

        if (item.Modified is { } modified)
        {
            properties["Modified"] = FormatTimestamp(modified);
        }

        if (item.Scanned is { } scanned)
        {
            properties["Scanned"] = FormatTimestamp(scanned);
        }

        switch (item.Kind)
        {
            case DiskCatalogMakerV5ItemKind.Package:
                properties["Package"] = "true";
                break;
            case DiskCatalogMakerV5ItemKind.Alias:
                properties["Alias"] = "true";
                break;
            case DiskCatalogMakerV5ItemKind.ArchiveMember:
                properties["ArchiveMember"] = "true";
                break;
        }

        if (item.AliasTargetItemId is { } aliasTargetItemId)
        {
            properties["AliasTargetItemId"] = aliasTargetItemId.ToString(CultureInfo.InvariantCulture);
            if (volumeItems.TryGetValue(aliasTargetItemId, out var target))
            {
                properties["AliasTargetPath"] = BuildPath(target, volumeItems, volume.Name);
            }
        }

        AddAliasProperties(item, volume, volumeItems, aliasesByItemId, properties);
        AddThumbnailProperties(item, thumbnailsByKey, thumbnailExistence, packageRoot, properties);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = item.Kind is DiskCatalogMakerV5ItemKind.Directory or DiskCatalogMakerV5ItemKind.Package
                ? "folder"
                : "file",
            ["name"] = item.Name,
            ["sizeBytes"] = item.SizeBytes.ToString(CultureInfo.InvariantCulture),
            ["properties"] = properties
        };
    }

    private static string? ResolveParentId(
        DiskCatalogMakerV5Item item,
        string mediaNodeId,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Item> volumeItems,
        List<string> warnings)
    {
        if (item.ParentItemId is not { } parentItemId)
        {
            return mediaNodeId;
        }

        if (!volumeItems.TryGetValue(parentItemId, out var parent))
        {
            AddWarning(warnings,
                $"item {item.ItemId} references missing parent {parentItemId}; it was attached to the volume root.");
            return mediaNodeId;
        }

        if (parent.ItemId == item.ItemId || HasCycle(item, volumeItems))
        {
            AddWarning(warnings,
                $"item {item.ItemId} participates in a parent cycle; it was attached to the volume root.");
            return mediaNodeId;
        }

        return CreateItemNodeId(parent.VolumeId, parent.ItemId);
    }

    private static void AddAliasProperties(
        DiskCatalogMakerV5Item item,
        DiskCatalogMakerV5Volume volume,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Item> volumeItems,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Alias> aliasesByItemId,
        Dictionary<string, object?> properties)
    {
        if (!aliasesByItemId.TryGetValue(item.ItemId, out var alias))
        {
            return;
        }

        if (alias.TargetVolumeId != 0)
        {
            properties["AliasTargetVolumeId"] = alias.TargetVolumeId.ToString(CultureInfo.InvariantCulture);
        }

        if (alias.TargetDirectoryId != 0)
        {
            properties["AliasTargetDirectoryId"] = alias.TargetDirectoryId.ToString(CultureInfo.InvariantCulture);
        }

        if (alias.TargetVolumeId == volume.VolumeId &&
            alias.TargetDirectoryId != 0 &&
            volumeItems.TryGetValue(alias.TargetDirectoryId, out var targetDirectory))
        {
            properties["AliasTargetPath"] = BuildPath(targetDirectory, volumeItems, volume.Name);
        }

        AddIfPresent(properties, "AliasMountPoint", alias.MountPoint);
        AddIfPresent(properties, "AliasPosixPath", alias.PosixPath);
        AddIfPresent(properties, "AliasServer", alias.Server);
        AddIfPresent(properties, "AliasUser", alias.User);
        AddIfPresent(properties, "AliasZone", alias.Zone);
        AddIfPresent(properties, "AliasUnicodeName", alias.UnicodeName);
        AddIfPresent(properties, "AliasUnicodeVolumeName", alias.UnicodeVolumeName);

        if (alias.VolumeCreated is { } volumeCreated)
        {
            properties["AliasVolumeCreated"] = FormatTimestamp(volumeCreated);
        }

        if (alias.FileCreated is { } fileCreated)
        {
            properties["AliasFileCreated"] = FormatTimestamp(fileCreated);
        }

        if (alias.RecursiveExtraBytes is { } extraBytes)
        {
            properties["AliasExtraBytes"] = extraBytes.Length.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void AddThumbnailProperties(
        DiskCatalogMakerV5Item item,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Thumbnail> thumbnailsByKey,
        Dictionary<string, bool> thumbnailExistence,
        string? packageRoot,
        Dictionary<string, object?> properties)
    {
        if (item.ThumbnailKey is not { } thumbnailKey)
        {
            return;
        }

        var extension = thumbnailsByKey.TryGetValue(thumbnailKey, out var thumbnail)
            ? thumbnail.Extension
            : null;
        var relativePath = DiskCatalogMakerV5ThumbnailResolver.CreateRelativePath(thumbnailKey, extension);
        properties["Thumbnail"] = relativePath;

        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            return;
        }

        if (!thumbnailExistence.TryGetValue(relativePath, out var resolved))
        {
            resolved = DiskCatalogMakerV5ThumbnailResolver.TryResolve(
                packageRoot, thumbnailKey, extension, out _, out _);
            thumbnailExistence[relativePath] = resolved;
        }

        properties["ThumbnailResolved"] = resolved ? "true" : "false";
    }

    private static bool HasCycle(
        DiskCatalogMakerV5Item item,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Item> volumeItems)
    {
        var seen = new HashSet<uint> { item.ItemId };
        var current = item;
        var steps = 0;
        while (current.ParentItemId is { } parentItemId)
        {
            if (!volumeItems.TryGetValue(parentItemId, out var parent))
            {
                return false;
            }

            if (!seen.Add(parentItemId))
            {
                return true;
            }

            current = parent;
            if (++steps > volumeItems.Count)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildPath(
        DiskCatalogMakerV5Item item,
        IReadOnlyDictionary<uint, DiskCatalogMakerV5Item> volumeItems,
        string volumeName)
    {
        if (!string.IsNullOrEmpty(item.FullPath))
        {
            return item.FullPath;
        }

        var names = new List<string>();
        var seen = new HashSet<uint>();
        var current = item;
        while (true)
        {
            names.Add(current.Name);
            if (current.ParentItemId is not { } parentItemId)
            {
                break;
            }

            if (!seen.Add(current.ItemId) || !volumeItems.TryGetValue(parentItemId, out var parent))
            {
                break;
            }

            current = parent;
        }

        names.Reverse();
        names.Insert(0, volumeName);
        return string.Join("/", names);
    }

    private static string CreateMediaNodeId(uint volumeId) => $"media:{volumeId}";

    private static string CreateItemNodeId(uint volumeId, uint itemId) => $"item:{volumeId}:{itemId}";

    private static void AddIfPresent(Dictionary<string, object?> properties, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            properties[key] = value;
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string DescribeItemKind(DiskCatalogMakerV5ItemKind kind) => kind switch
    {
        DiskCatalogMakerV5ItemKind.Directory => "Folder",
        DiskCatalogMakerV5ItemKind.File => "File",
        DiskCatalogMakerV5ItemKind.Package => "Package",
        DiskCatalogMakerV5ItemKind.Alias => "Alias",
        DiskCatalogMakerV5ItemKind.ArchiveMember => "Archive member",
        _ => "Item"
    };

    private static string DescribeVolumeKind(DiskCatalogMakerV5VolumeKind kind) => kind switch
    {
        DiskCatalogMakerV5VolumeKind.Disk => "Disk",
        DiskCatalogMakerV5VolumeKind.CompactDisc => "CD",
        DiskCatalogMakerV5VolumeKind.Dvd => "DVD",
        DiskCatalogMakerV5VolumeKind.Network => "Network",
        DiskCatalogMakerV5VolumeKind.Folder => "Folder",
        DiskCatalogMakerV5VolumeKind.DiskImage => "Disk image",
        _ => "Unknown"
    };

    private static string DescribeFileSystem(DiskCatalogMakerV5FileSystem fileSystem) => fileSystem switch
    {
        DiskCatalogMakerV5FileSystem.Hfs => "HFS",
        DiskCatalogMakerV5FileSystem.HfsPlus => "HFS+",
        DiskCatalogMakerV5FileSystem.Apfs => "APFS",
        DiskCatalogMakerV5FileSystem.Fat => "FAT",
        DiskCatalogMakerV5FileSystem.Ntfs => "NTFS",
        DiskCatalogMakerV5FileSystem.Udf => "UDF",
        DiskCatalogMakerV5FileSystem.Iso9660 => "ISO 9660",
        DiskCatalogMakerV5FileSystem.Network => "Network",
        _ => "Unknown"
    };

    private static string DescribeLabel(int label) => label switch
    {
        1 => "Orange",
        2 => "Red",
        3 => "Yellow",
        4 => "Blue",
        5 => "Purple",
        6 => "Green",
        7 => "Gray",
        _ => "None"
    };

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < MaximumHierarchyWarnings)
        {
            warnings.Add(warning);
        }
        else if (warnings.Count == MaximumHierarchyWarnings)
        {
            warnings.Add("Additional hierarchy warnings were suppressed.");
        }
    }
}
