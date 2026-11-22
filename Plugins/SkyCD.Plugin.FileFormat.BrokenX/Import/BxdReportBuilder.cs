using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SkyCD.Plugin.FileFormat.BrokenX.Exceptions;
using SkyCD.Plugin.FileFormat.BrokenX.Models;
using SkyCD.Plugin.FileFormat.BrokenX.Parsing;

namespace SkyCD.Plugin.FileFormat.BrokenX.Import;

/// <summary>
/// Converts between <see cref="BxdDatabase"/> trees and the shared flat node rows.
/// </summary>
/// <remarks>
/// All preserved raw fields travel as plain strings so rows survive host serialization:
/// base flags and fixed fields as invariant integers, name bytes and text/blob payloads as
/// Base64, and the property order as a descriptor list. Unknown descriptors are reported,
/// never dropped.
/// </remarks>
public static class BxdReportBuilder
{
    private const string BaseFlagsKey = "BxdBaseFlags";
    private const string Field14Key = "BxdField14";
    private const string Field18Key = "BxdField18";
    private const string NameBytesKey = "BxdNameBytes";
    private const string PropOrderKey = "BxdPropOrder";
    private const string RootPropOrderKey = "BxdRootPropOrder";
    private const string PropPrefix = "BxdProp_";
    private const string RootPropPrefix = "BxdRootProp_";

    /// <summary>
    /// Flattens a database into node rows.
    /// </summary>
    public static BxdReportDocument Build(BxdDatabase database, string fileStem, int totalRecordsRead)
    {
        ArgumentNullException.ThrowIfNull(database);

        var mediaName = string.IsNullOrWhiteSpace(fileStem) ? "Broken X Database" : fileStem.Trim();
        var rows = new List<Dictionary<string, object?>>();
        var unknownDescriptors = new SortedSet<string>(StringComparer.Ordinal);

        var mediaId = $"media:{mediaName}";
        var mediaProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["BxdSource"] = "brokenx-native"
        };
        WriteProperties(mediaProperties, database.RootProperties, RootPropPrefix, RootPropOrderKey, unknownDescriptors);
        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = mediaId,
            ["parentId"] = null,
            ["kind"] = "media",
            ["name"] = mediaName,
            ["sizeBytes"] = "0",
            ["properties"] = mediaProperties
        });

        var imported = 0;
        var counter = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var root in database.Roots)
        {
            AppendItem(rows, root, mediaId, mediaName, mediaName, unknownDescriptors, counter, ref imported);
        }

        var summary = new BxdImportSummary(
            TotalRecordsRead: totalRecordsRead,
            ImportedRecordsCount: imported,
            SkippedRecordsCount: 0,
            IsPartialReport: false,
            SkippedReasons: [],
            UnsupportedFields: unknownDescriptors.ToList(),
            DetectedVersion: $"0x{BxdFormat.SupportedVersion:X2}");

        return new BxdReportDocument(rows, summary);
    }

    private static void AppendItem(
        List<Dictionary<string, object?>> rows,
        BxdItem item,
        string parentId,
        string mediaName,
        string currentPath,
        SortedSet<string> unknownDescriptors,
        Dictionary<string, int> counter,
        ref int imported)
    {
        var kind = item.Children.Count > 0 ? "folder" : "file";
        var nodePath = $"{currentPath}/{item.Name}";
        var nodeId = $"{kind}:{nodePath}";
        if (!counter.TryAdd(nodeId, 0))
        {
            counter[nodeId]++;
            nodeId = $"{nodeId}#{counter[nodeId]}";
        }

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [BaseFlagsKey] = ((int)(item.Flags & ~BxdFormat.StructuralMask)).ToString(CultureInfo.InvariantCulture),
            [Field14Key] = item.Field14.ToString(CultureInfo.InvariantCulture),
            [Field18Key] = item.Field18.ToString(CultureInfo.InvariantCulture),
            [NameBytesKey] = Convert.ToBase64String(item.NameBytes ?? Encoding.UTF8.GetBytes(item.Name))
        };
        WriteProperties(properties, item.Metadata, PropPrefix, PropOrderKey, unknownDescriptors);

        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = kind,
            ["name"] = item.Name,
            ["sizeBytes"] = "0",
            ["properties"] = properties
        });
        imported++;

        foreach (var child in item.Children)
        {
            AppendItem(rows, child, nodeId, mediaName, nodePath, unknownDescriptors, counter, ref imported);
        }
    }

    private static void WriteProperties(
        Dictionary<string, object?> target,
        List<BxdProperty> properties,
        string prefix,
        string orderKey,
        SortedSet<string> unknownDescriptors)
    {
        var order = new List<string>();
        foreach (var property in properties)
        {
            var key = $"{prefix}{property.Descriptor:X2}";
            order.Add($"{property.Descriptor:X2}");
            unknownDescriptors.Add($"0x{property.Descriptor:X2}");
            target[key] = property.Kind switch
            {
                BxdPropertyKind.Uint32 => $"u:{property.UintValue.ToString(CultureInfo.InvariantCulture)}",
                BxdPropertyKind.Text => $"s:{Convert.ToBase64String(property.RawBytes ?? [])}",
                BxdPropertyKind.Blob => $"b:{Convert.ToBase64String(property.RawBytes ?? [])}",
                _ => throw new BxdCorruptException($"unknown property kind for descriptor 0x{property.Descriptor:X2}.")
            };
        }

        target[orderKey] = string.Join(",", order);
    }

    /// <summary>
    /// Rebuilds a database from flat node rows (round-trip and fresh writes).
    /// </summary>
    /// <remarks>
    /// Rows carrying preserved <c>Bxd*</c> properties restore their raw fields verbatim; other rows
    /// are written with neutral defaults. Orphaned rows (dangling parent links) attach to the database
    /// root instead of being dropped.
    /// </remarks>
    public static BxdDatabase ToDatabase(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var database = new BxdDatabase();
        var itemsById = new Dictionary<string, (BxdItem Item, string? ParentId)>(StringComparer.Ordinal);
        var rowOrder = new List<string>();

        var mediaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.Equals(row.TryGetValue("kind", out var kindValue) ? kindValue?.ToString() : null, "media", StringComparison.OrdinalIgnoreCase) &&
                row.TryGetValue("nodeId", out var mediaIdValue) && mediaIdValue?.ToString() is { } mediaId)
            {
                mediaIds.Add(mediaId);
            }
        }

        foreach (var row in rows)
        {
            var nodeId = row.TryGetValue("nodeId", out var idValue) ? idValue?.ToString() : null;
            var preserved = row.TryGetValue("properties", out var preservedValue) &&
                preservedValue is Dictionary<string, object?> preservedDict
                    ? preservedDict
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(nodeId) || mediaIds.Contains(nodeId) || !itemsById.TryAdd(nodeId, (new BxdItem
            {
                Flags = (ushort)(ReadBaseFlags(preserved) & ~BxdFormat.StructuralMask),
                Name = row.TryGetValue("name", out var nameValue) ? nameValue?.ToString() ?? nodeId : nodeId,
                NameBytes = ReadNameBytes(preserved),
                Field14 = ReadUInt(preserved, Field14Key),
                Field18 = ReadUInt(preserved, Field18Key)
            }, row.TryGetValue("parentId", out var parentValue) ? parentValue?.ToString() : null)))
            {
                continue;
            }

            rowOrder.Add(nodeId);
            ReadProperties(row, PropPrefix, PropOrderKey, itemsById[nodeId].Item.Metadata);
        }

        // The database root carries the first media row's root properties, or canonical defaults.
        var mediaRow = rows.FirstOrDefault(r =>
            string.Equals(r.TryGetValue("kind", out var kindValue) ? kindValue?.ToString() : null, "media", StringComparison.OrdinalIgnoreCase));
        if (mediaRow is not null)
        {
            ReadProperties(mediaRow, RootPropPrefix, RootPropOrderKey, database.RootProperties);
        }

        if (database.RootProperties.Count == 0)
        {
            database.RootProperties.Add(new BxdProperty(0x42, 1u));
            database.RootProperties.Add(new BxdProperty(0x4E, 1u));
            database.RootProperties.Add(new BxdProperty(0x4F, 0x04150953u));
        }

        foreach (var nodeId in rowOrder)
        {
            var (item, parentId) = itemsById[nodeId];
            if (!string.IsNullOrWhiteSpace(parentId) && !mediaIds.Contains(parentId) && itemsById.TryGetValue(parentId, out var parent))
            {
                parent.Item.Children.Add(item);
            }
            else
            {
                database.Roots.Add(item);
            }
        }

        return database;
    }

    private static int ReadBaseFlags(Dictionary<string, object?> row)
    {
        if (row.TryGetValue(BaseFlagsKey, out var value) &&
            int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
        {
            return flags & 0xFFFF;
        }

        return 0;
    }

    private static uint ReadUInt(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var value) &&
            uint.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0u;
    }

    private static byte[]? ReadNameBytes(Dictionary<string, object?> row)
    {
        if (row.TryGetValue(NameBytesKey, out var value))
        {
            try
            {
                return Convert.FromBase64String(value?.ToString() ?? string.Empty);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static void ReadProperties(
        Dictionary<string, object?> row,
        string prefix,
        string orderKey,
        List<BxdProperty> target)
    {
        if (!row.TryGetValue("properties", out var propsValue) ||
            propsValue is not Dictionary<string, object?> properties)
        {
            return;
        }

        var order = properties.TryGetValue(orderKey, out var orderValue)
            ? (orderValue?.ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
            : properties.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Select(k => k[prefix.Length..])
                .ToArray();

        foreach (var descriptorText in order)
        {
            if (descriptorText.Length != 2 ||
                !byte.TryParse(descriptorText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var descriptor))
            {
                continue;
            }

            if (!properties.TryGetValue(prefix + descriptorText, out var encodedValue))
            {
                continue;
            }

            var encoded = encodedValue?.ToString() ?? string.Empty;
            if (encoded.Length < 2 || encoded[1] != ':')
            {
                continue;
            }

            var payload = encoded[2..];
            switch (encoded[0])
            {
                case 'u' when uint.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue):
                    target.Add(new BxdProperty(descriptor, uintValue));
                    break;
                case 's':
                    try
                    {
                        var raw = Convert.FromBase64String(payload);
                        target.Add(new BxdProperty(descriptor, StrictUtf8.GetString(raw), raw));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is DecoderFallbackException || ex is ArgumentException)
                    {
                        // Leniency on the write path only: unreadable preserved values are dropped.
                    }

                    break;
                case 'b':
                    try
                    {
                        target.Add(new BxdProperty(descriptor, Convert.FromBase64String(payload)));
                    }
                    catch (FormatException)
                    {
                        // Leniency on the write path only: unreadable preserved values are dropped.
                    }

                    break;
            }
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
