using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Plugin.Adc.Models;

namespace SkyCD.Plugin.Adc.Import;

/// <summary>
/// Converts a validated <see cref="AdcCatalog"/> into the shared flat node rows.
/// </summary>
/// <remarks>
/// Each cataloged volume becomes a <c>media</c> row; items below it become <c>folder</c> or
/// <c>file</c> rows in stable preorder. Typed fields (size, timestamps, comments, categories,
/// attributes, type code) are mapped to properties; every preserved optional property keeps
/// its descriptor in the <c>AdcProp_</c> extension bag.
/// </remarks>
public static class AdcReportBuilder
{
    private const string PropertyPrefix = "AdcProp_";
    private const string PropertyOrderKey = "AdcPropertyOrder";

    public static AdcReportDocument Build(AdcCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var rows = new List<Dictionary<string, object?>>(
            catalog.Volumes.Count + catalog.Items.Count);
        var categoryNames = new Dictionary<uint, string>(catalog.Categories.Count);
        foreach (var category in catalog.Categories)
        {
            categoryNames[category.CategoryId] = category.Name;
        }

        var children = new Dictionary<(uint VolumeId, uint ParentId), List<AdcItem>>();
        foreach (var item in catalog.Items)
        {
            var key = (item.VolumeId, item.ParentId);
            if (!children.TryGetValue(key, out var siblings))
            {
                siblings = [];
                children[key] = siblings;
            }

            siblings.Add(item);
        }

        foreach (var volume in catalog.Volumes)
        {
            var mediaId = $"media:{volume.VolumeId}";
            rows.Add(BuildVolumeRow(volume, mediaId));

            var stack = new Stack<AdcItem>();
            AddChildren(children, stack, volume.VolumeId, 0);
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                var kind = item.Kind == AdcItemKind.Folder ? "folder" : "file";
                var nodeId = $"{kind}:{item.ItemId}";
                var parentId = item.ParentId == 0 ? mediaId : $"folder:{item.ParentId}";
                rows.Add(BuildItemRow(item, nodeId, parentId, catalog, categoryNames));
                AddChildren(children, stack, volume.VolumeId, item.ItemId);
            }
        }

        var summary = new AdcImportSummary(
            TotalRecordsRead: catalog.TotalRecordCount,
            ImportedRecordsCount: catalog.Volumes.Count + catalog.Items.Count,
            SkippedRecordsCount: 0,
            IsPartialReport: false,
            SkippedReasons: [],
            UnsupportedFields: catalog.UnsupportedFields,
            DetectedVersion: $"{catalog.VersionMajor}.{catalog.VersionMinor}",
            VolumeCount: catalog.Volumes.Count,
            ItemCount: catalog.Items.Count,
            CategoryCount: catalog.Categories.Count,
            CommentCount: catalog.Comments.Count);

        return new AdcReportDocument(rows, summary);
    }

    private static Dictionary<string, object?> BuildVolumeRow(AdcVolume volume, string mediaId)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AdcSource"] = "advanced-disk-catalog",
            ["AdcVolumeId"] = volume.VolumeId.ToString(CultureInfo.InvariantCulture),
            ["AdcVolumeType"] = DescribeVolumeType(volume.VolumeType),
            ["AdcVolumeTypeCode"] = ((byte)volume.VolumeType).ToString(CultureInfo.InvariantCulture),
            ["AdcVolumeFlags"] = volume.Flags.ToString(CultureInfo.InvariantCulture),
            ["AdcSerialNumber"] = $"0x{volume.SerialNumber:X8}",
            ["AdcCapacityBytes"] = volume.CapacityBytes.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(volume.Source))
        {
            properties["AdcSourcePath"] = volume.Source;
        }

        WriteProperties(properties, volume.Properties);

        var name = string.IsNullOrWhiteSpace(volume.Label)
            ? $"Volume {volume.VolumeId.ToString(CultureInfo.InvariantCulture)}"
            : volume.Label;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = mediaId,
            ["parentId"] = null,
            ["kind"] = "media",
            ["name"] = name,
            ["sizeBytes"] = "0",
            ["properties"] = properties
        };
    }

    private static Dictionary<string, object?> BuildItemRow(
        AdcItem item,
        string nodeId,
        string parentId,
        AdcCatalog catalog,
        Dictionary<uint, string> categoryNames)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AdcItemId"] = item.ItemId.ToString(CultureInfo.InvariantCulture),
            ["AdcVolumeId"] = item.VolumeId.ToString(CultureInfo.InvariantCulture),
            ["AdcAttributes"] = item.Attributes.ToString(CultureInfo.InvariantCulture),
            ["AdcTypeCode"] = item.TypeCode.ToString(CultureInfo.InvariantCulture)
        };

        if (item.Attributes != 0)
        {
            properties["AdcAttributeNames"] = DescribeAttributes(item.Attributes);
        }

        if (item.CommentIndex != 0)
        {
            properties["Comments"] = catalog.Comments[(int)item.CommentIndex - 1];
        }

        if (item.CategoryIds.Count > 0)
        {
            var names = new List<string>(item.CategoryIds.Count);
            var ids = new List<string>(item.CategoryIds.Count);
            foreach (var categoryId in item.CategoryIds)
            {
                names.Add(categoryNames[categoryId]);
                ids.Add(categoryId.ToString(CultureInfo.InvariantCulture));
            }

            properties["Categories"] = string.Join("; ", names);
            properties["AdcCategoryIds"] = string.Join(",", ids);
        }

        AddTimestamp(properties, "AdcCreatedUtc", item.CreatedFileTime);
        AddTimestamp(properties, "AdcModifiedUtc", item.ModifiedFileTime);
        AddTimestamp(properties, "AdcAccessedUtc", item.AccessedFileTime);
        WriteProperties(properties, item.Properties);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = item.Kind == AdcItemKind.Folder ? "folder" : "file",
            ["name"] = item.Name,
            ["sizeBytes"] = item.SizeBytes.ToString(CultureInfo.InvariantCulture),
            ["properties"] = properties
        };
    }

    private static void AddChildren(
        Dictionary<(uint VolumeId, uint ParentId), List<AdcItem>> children,
        Stack<AdcItem> stack,
        uint volumeId,
        uint parentId)
    {
        if (!children.TryGetValue((volumeId, parentId), out var items))
        {
            return;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            stack.Push(items[i]);
        }
    }

    private static void AddTimestamp(Dictionary<string, object?> properties, string key, ulong fileTime)
    {
        if (fileTime == 0)
        {
            return;
        }

        properties[key] = DateTime.FromFileTimeUtc((long)fileTime)
            .ToString("O", CultureInfo.InvariantCulture);
    }

    private static void WriteProperties(
        Dictionary<string, object?> target,
        List<AdcProperty> properties)
    {
        if (properties.Count == 0)
        {
            return;
        }

        var order = new List<string>(properties.Count);
        foreach (var property in properties)
        {
            var key = $"{PropertyPrefix}{property.Id:X2}";
            order.Add($"{property.Id:X2}");
            target[key] = property.Kind switch
            {
                AdcPropertyKind.Uint32 => $"u:{property.UintValue.ToString(CultureInfo.InvariantCulture)}",
                AdcPropertyKind.String => $"s:{property.Text}",
                AdcPropertyKind.Blob => $"b:{Convert.ToBase64String(property.Blob ?? [])}",
                _ => throw new InvalidOperationException($"unknown property kind {property.Kind}.")
            };
        }

        target[PropertyOrderKey] = string.Join(",", order);
    }

    private static string DescribeVolumeType(AdcVolumeType volumeType) =>
        volumeType switch
        {
            AdcVolumeType.Fixed => "Fixed",
            AdcVolumeType.Removable => "Removable",
            AdcVolumeType.Network => "Network",
            AdcVolumeType.CdRom => "CD-ROM",
            AdcVolumeType.Dvd => "DVD",
            AdcVolumeType.RamDisk => "RAM disk",
            _ => "Unknown"
        };

    private static string DescribeAttributes(byte attributes)
    {
        string[] names =
        [
            "ReadOnly",
            "Hidden",
            "System",
            "Volume",
            "Directory",
            "Archive",
            "Device",
            "Normal"
        ];

        var set = new List<string>();
        for (var bit = 0; bit < names.Length; bit++)
        {
            if ((attributes & (1 << bit)) != 0)
            {
                set.Add(names[bit]);
            }
        }

        return string.Join("; ", set);
    }
}
