using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Export;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Import;

/// <summary>
/// Maps a DED object graph to the host's flat catalog rows and back. Rows carry
/// typed <c>Ded*</c> properties so opaque flags, extensions, payloads, raw
/// unknown records, and full 64-bit sizes survive a row-only round trip.
/// </summary>
public static class DedCatalogMapper
{
    public const string NodeIdPrefix = "ded:";
    public const string ObjectIdProperty = "DedObjectId";
    public const string ObjectKindProperty = "DedObjectKind";
    public const string KindCodeProperty = "DedKindCode";
    public const string FlagsProperty = "DedFlags";
    public const string ParentIdProperty = "DedParentId";
    public const string PathProperty = "DedPath";
    public const string CreatedProperty = "DedCreatedFileTime";
    public const string ModifiedProperty = "DedModifiedFileTime";
    public const string AddedProperty = "DedAddedFileTime";
    public const string LogicalSizeProperty = "DedLogicalSize";
    public const string AggregateSizeProperty = "DedAggregateSize";
    public const string FileCountProperty = "DedFileCount";
    public const string FolderCountProperty = "DedFolderCount";
    public const string CategoryIdsProperty = "DedCategoryIds";
    public const string ExtensionsProperty = "DedExtensions";
    public const string PayloadsProperty = "DedPayloads";
    public const string TailProperty = "DedTail";
    public const string RawRecordProperty = "DedRawRecord";
    public const string NameEncodingProperty = "DedNameEncoding";
    public const string PathEncodingProperty = "DedPathEncoding";
    public const string DescriptionEncodingProperty = "DedDescriptionEncoding";
    public const string HeaderFlagsProperty = "DedHeaderFlags";
    public const string ReservedFlagsProperty = "DedReservedFlags";
    public const string CreationTimeProperty = "DedCreationFileTime";
    public const string TableSlotsProperty = "DedTableSlotCount";
    public const string DatabaseTitleProperty = "DedDatabaseTitle";

    /// <summary>Builds host rows and an import summary from a database.</summary>
    public static DedReportDocument BuildRows(DedDatabase database, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        var summary = new DedImportSummary { Title = database.Title };
        var children = BuildChildrenMap(database);
        var effectiveParent = new Dictionary<ulong, ulong>();
        foreach (var (parentId, childIds) in children)
        {
            foreach (var childId in childIds)
            {
                effectiveParent[childId] = parentId;
            }
        }

        var ordered = OrderObjects(database, children);
        var rows = new List<Dictionary<string, object?>>(ordered.Count);
        foreach (var item in ordered)
        {
            var row = BuildRow(database, item, effectiveParent, children);
            rows.Add(row);
            switch (item.Kind)
            {
                case DedObjectKind.Database:
                    break;
                case DedObjectKind.CategoryFolder:
                    summary.Categories++;
                    break;
                case DedObjectKind.Volume:
                    summary.Volumes++;
                    break;
                case DedObjectKind.Folder:
                    summary.Folders++;
                    break;
                case DedObjectKind.Archive:
                    summary.Archives++;
                    break;
                case DedObjectKind.File:
                    summary.Files++;
                    break;
                default:
                    summary.UnknownObjects++;
                    break;
            }
        }

        summary.Objects = ordered.Count;
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            summary.Notes.Add($"Source: {sourceName}");
        }

        return new DedReportDocument(rows, database, summary);
    }

    /// <summary>Builds a database from host rows.</summary>
    public static DedDatabase ToDatabase(IReadOnlyList<Dictionary<string, object?>> rows, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var database = new DedDatabase();
        var pending = new List<PendingRow>(rows.Count);
        var byKey = new Dictionary<string, PendingRow>(StringComparer.Ordinal);
        var usedIds = new HashSet<ulong>();
        ulong highestId = 0;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var properties = ReadProperties(row);
            var key = ReadString(row, "id") ?? ReadString(row, "nodeId") ?? $"row:{index}";
            var item = new PendingRow(key, row, properties, ReadString(row, "parentId"));
            if (!byKey.TryAdd(key, item))
            {
                throw new DedModelException("catalog rows contain duplicate node ids.");
            }

            item.ObjectId = ReadUlong(properties, ObjectIdProperty)
                ?? ReadRawRecordId(properties);
            if (item.ObjectId is { } explicitId)
            {
                if (explicitId == 0 || !usedIds.Add(explicitId))
                {
                    throw new DedModelException("catalog rows contain duplicate or zero object ids.");
                }

                highestId = Math.Max(highestId, explicitId);
            }

            pending.Add(item);
        }

        foreach (var item in pending)
        {
            if (item.ObjectId is null)
            {
                var assigned = highestId + 1;
                while (!usedIds.Add(assigned))
                {
                    assigned++;
                }

                highestId = assigned;
                item.ObjectId = assigned;
            }
        }

        foreach (var item in pending)
        {
            var parent = ResolveParent(item.ParentKey, byKey);
            var value = CreateObject(item, parent);
            database.Objects.Add(value);
            item.Created = value;
        }

        foreach (var item in pending)
        {
            var parent = ResolveParent(item.ParentKey, byKey);
            if (parent?.Created is not null && item.Created is not null && item.Created.ParentId == parent.ObjectId)
            {
                parent.Created.ChildIds.Add(item.Created.ObjectId);
            }
        }

        ApplyDatabaseMetadata(database, pending);
        if (!string.IsNullOrWhiteSpace(title))
        {
            database.Title = title!;
        }

        return database;
    }

    private static Dictionary<ulong, List<ulong>> BuildChildrenMap(DedDatabase database)
    {
        var existing = new HashSet<ulong>(database.Objects.Count);
        foreach (var item in database.Objects)
        {
            existing.Add(item.ObjectId);
        }

        var children = new Dictionary<ulong, List<ulong>>(database.Objects.Count);
        var linked = new HashSet<ulong>();
        foreach (var item in database.Objects)
        {
            var list = new List<ulong>(item.ChildIds.Count);
            foreach (var childId in item.ChildIds)
            {
                if (existing.Contains(childId) && linked.Add(childId))
                {
                    list.Add(childId);
                }
            }

            children[item.ObjectId] = list;
        }

        foreach (var item in database.Objects)
        {
            if (item.ParentId == 0 || !existing.Contains(item.ParentId))
            {
                continue;
            }

            if (linked.Add(item.ObjectId))
            {
                children[item.ParentId].Add(item.ObjectId);
            }
        }

        return children;
    }

    private static List<DedCatalogObject> OrderObjects(
        DedDatabase database,
        Dictionary<ulong, List<ulong>> children)
    {
        var byId = new Dictionary<ulong, DedCatalogObject>(database.Objects.Count);
        foreach (var item in database.Objects)
        {
            byId[item.ObjectId] = item;
        }

        var ordered = new List<DedCatalogObject>(database.Objects.Count);
        var visited = new HashSet<ulong>();
        var queue = new Queue<ulong>();
        foreach (var item in database.Objects)
        {
            if (item.ParentId == 0 || !byId.ContainsKey(item.ParentId))
            {
                if (visited.Add(item.ObjectId))
                {
                    queue.Enqueue(item.ObjectId);
                }
            }
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var item = byId[id];
            ordered.Add(item);
            foreach (var childId in children[id])
            {
                if (visited.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        foreach (var item in database.Objects)
        {
            if (visited.Add(item.ObjectId))
            {
                ordered.Add(item);
            }
        }

        return ordered;
    }

    private static Dictionary<string, object?> BuildRow(
        DedDatabase database,
        DedCatalogObject item,
        Dictionary<ulong, ulong> effectiveParent,
        Dictionary<ulong, List<ulong>> children)
    {
        var nodeId = NodeId(item.ObjectId);
        string? parentId = effectiveParent.TryGetValue(item.ObjectId, out var parentObjectId)
            ? NodeId(parentObjectId)
            : null;

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ObjectIdProperty] = item.ObjectId.ToString(CultureInfo.InvariantCulture),
            [ObjectKindProperty] = DedObjectKinds.ToText(item.Kind),
            [KindCodeProperty] = item.RawKind.ToString(CultureInfo.InvariantCulture),
            [FlagsProperty] = item.Flags.ToString(CultureInfo.InvariantCulture),
            [ParentIdProperty] = item.ParentId.ToString(CultureInfo.InvariantCulture),
            [CreatedProperty] = item.CreatedFileTime.ToString(CultureInfo.InvariantCulture),
            [ModifiedProperty] = item.ModifiedFileTime.ToString(CultureInfo.InvariantCulture),
            [AddedProperty] = item.AddedFileTime.ToString(CultureInfo.InvariantCulture),
            [LogicalSizeProperty] = item.LogicalSize.ToString(CultureInfo.InvariantCulture),
            [AggregateSizeProperty] = item.AggregateSize.ToString(CultureInfo.InvariantCulture),
            [FileCountProperty] = item.FileCount.ToString(CultureInfo.InvariantCulture),
            [FolderCountProperty] = item.FolderCount.ToString(CultureInfo.InvariantCulture),
            [NameEncodingProperty] = EncodingText(item.Name.Encoding),
            [PathEncodingProperty] = EncodingText(item.Path.Encoding),
            [DescriptionEncodingProperty] = EncodingText(item.Description.Encoding)
        };

        if (!item.Path.IsEmpty)
        {
            properties[PathProperty] = item.Path.Value;
        }

        if (!item.Description.IsEmpty)
        {
            properties["Description"] = item.Description.Value;
        }

        var date = DedFileTime.ToDateText(item.ModifiedFileTime);
        if (date is not null)
        {
            properties["Date"] = date;
            properties["Time"] = DedFileTime.ToTimeText(item.ModifiedFileTime)!;
        }

        if (item.CategoryIds.Count > 0)
        {
            properties[CategoryIdsProperty] = string.Join(",", item.CategoryIds.ConvertAll(
                id => id.ToString(CultureInfo.InvariantCulture)));
        }

        if (item.Extensions.Count > 0)
        {
            properties[ExtensionsProperty] = Convert.ToBase64String(
                DedRecordSerializer.SerializeExtensions(item.Extensions));
        }

        if (item.Payloads.Count > 0)
        {
            properties[PayloadsProperty] = Convert.ToBase64String(
                DedRecordSerializer.SerializePayloads(item.Payloads));
        }

        if (item.Tail is { Length: > 0 })
        {
            properties[TailProperty] = Convert.ToBase64String(item.Tail);
        }

        if (item.IsUnknownKind && item.RawRecord is not null)
        {
            properties[RawRecordProperty] = Convert.ToBase64String(item.RawRecord);
        }

        if (item.Kind == DedObjectKind.Database)
        {
            properties[HeaderFlagsProperty] = database.HeaderFlags.ToString(CultureInfo.InvariantCulture);
            properties[ReservedFlagsProperty] = database.ReservedFlags.ToString(CultureInfo.InvariantCulture);
            properties[CreationTimeProperty] = database.CreationFileTime.ToString(CultureInfo.InvariantCulture);
            properties[TableSlotsProperty] = database.TableSlotCount.ToString(CultureInfo.InvariantCulture);
            properties[DatabaseTitleProperty] = database.Title;
        }

        var isContainer = item.Kind is DedObjectKind.Database or DedObjectKind.CategoryFolder
            or DedObjectKind.Volume or DedObjectKind.Folder;
        var size = isContainer
            ? (item.AggregateSize != 0 ? item.AggregateSize : item.LogicalSize)
            : item.LogicalSize;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nodeId,
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = item.Kind switch
            {
                DedObjectKind.Volume => "media",
                DedObjectKind.Database or DedObjectKind.CategoryFolder or DedObjectKind.Folder => "folder",
                _ => "file"
            },
            ["name"] = ResolveName(item),
            ["sizeBytes"] = size.ToString(CultureInfo.InvariantCulture),
            ["childrenCount"] = children[item.ObjectId].Count.ToString(CultureInfo.InvariantCulture),
            ["properties"] = properties
        };
    }

    private static string ResolveName(DedCatalogObject item)
    {
        if (!string.IsNullOrWhiteSpace(item.Name.Value))
        {
            return item.Name.Value;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Object {item.ObjectId}");
    }

    private static string EncodingText(DedStringEncoding encoding) =>
        encoding == DedStringEncoding.Utf8 ? "utf8" : "ansi";

    private static string NodeId(ulong objectId) =>
        NodeIdPrefix + objectId.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> ReadProperties(Dictionary<string, object?> row)
    {
        foreach (var key in new[] { "properties", "Properties" })
        {
            if (row.TryGetValue(key, out var raw) && raw is IDictionary<string, object?> dictionary)
            {
                return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static PendingRow? ResolveParent(string? parentKey, Dictionary<string, PendingRow> byKey)
    {
        if (string.IsNullOrWhiteSpace(parentKey) || parentKey == "-1")
        {
            return null;
        }

        return byKey.TryGetValue(parentKey, out var parent) ? parent : null;
    }

    private static DedCatalogObject CreateObject(PendingRow item, PendingRow? parent)
    {
        var properties = item.Properties;
        var rawRecord = ReadBase64(properties, RawRecordProperty);
        var objectId = item.ObjectId ?? 0;
        var parentId = parent?.ObjectId ?? 0;

        var result = new DedCatalogObject
        {
            ObjectId = objectId,
            ParentId = parentId,
            Flags = ReadUint(properties, FlagsProperty) ?? 0,
            LogicalSize = ReadUlong(properties, LogicalSizeProperty)
                ?? ReadUlong(item.Row, "size", "sizeBytes") ?? 0,
            AggregateSize = ReadUlong(properties, AggregateSizeProperty) ?? 0,
            CreatedFileTime = ReadUlong(properties, CreatedProperty) ?? 0,
            ModifiedFileTime = ReadUlong(properties, ModifiedProperty) ?? 0,
            AddedFileTime = ReadUlong(properties, AddedProperty) ?? 0,
            FileCount = ReadUint(properties, FileCountProperty) ?? 0,
            FolderCount = ReadUint(properties, FolderCountProperty) ?? 0
        };

        var name = ReadString(item.Row, "name") ?? string.Empty;
        var path = ReadString(properties, PathProperty) ?? ReadString(item.Row, "path", "fullPath") ?? string.Empty;
        var description = ReadString(properties, "Description") ?? ReadString(properties, "Comment") ?? string.Empty;
        result.Name = ReadText(name, properties, NameEncodingProperty);
        result.Path = ReadText(path, properties, PathEncodingProperty);
        result.Description = ReadText(description, properties, DescriptionEncodingProperty);

        if (rawRecord is not null && rawRecord.Length >= 20)
        {
            result.RawKind = ReadUInt32(rawRecord, 0);
            result.ObjectId = ReadUInt64(rawRecord, 12);
            result.ParentId = ReadUInt64(rawRecord, 20);
            result.RawRecord = rawRecord;
        }
        else
        {
            result.RawKind = ResolveKind(item);
        }

        var categories = ReadString(properties, CategoryIdsProperty);
        if (!string.IsNullOrWhiteSpace(categories))
        {
            foreach (var value in categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var categoryId))
                {
                    result.CategoryIds.Add(categoryId);
                }
            }
        }

        result.Extensions.AddRange(DedRecordSerializer.ParseExtensions(
            ReadBase64(properties, ExtensionsProperty)));
        result.Payloads.AddRange(DedRecordSerializer.ParsePayloads(
            ReadBase64(properties, PayloadsProperty)));
        result.Tail = ReadBase64(properties, TailProperty);
        return result;
    }

    private static uint ResolveKind(PendingRow item)
    {
        if (DedObjectKinds.TryParse(ReadString(item.Properties, ObjectKindProperty), out var parsed))
        {
            return (uint)parsed;
        }

        if (ReadUint(item.Properties, KindCodeProperty) is { } raw)
        {
            return raw;
        }

        var kind = (ReadString(item.Row, "type") ?? ReadString(item.Row, "kind") ?? string.Empty)
            .ToLowerInvariant();
        return kind switch
        {
            "folder" => (uint)DedObjectKind.Folder,
            "media" => (uint)DedObjectKind.Volume,
            _ => (uint)DedObjectKind.File
        };
    }

    private static DedText ReadText(string value, Dictionary<string, object?> properties, string encodingProperty)
    {
        var encoding = ReadString(properties, encodingProperty);
        if (string.Equals(encoding, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            return DedText.FromUtf8(value);
        }

        if (string.Equals(encoding, "ansi", StringComparison.OrdinalIgnoreCase))
        {
            return DedText.FromAnsi(value);
        }

        return DedText.FromString(value);
    }

    private static void ApplyDatabaseMetadata(DedDatabase database, List<PendingRow> pending)
    {
        foreach (var item in pending)
        {
            var properties = item.Properties;
            var title = ReadString(properties, DatabaseTitleProperty);
            if (title is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(database.Title))
            {
                database.Title = title;
            }

            database.HeaderFlags = ReadUint(properties, HeaderFlagsProperty) ?? database.HeaderFlags;
            database.ReservedFlags = ReadUint(properties, ReservedFlagsProperty) ?? database.ReservedFlags;
            database.CreationFileTime = ReadUlong(properties, CreationTimeProperty) ?? database.CreationFileTime;
            database.TableSlotCount = ReadUint(properties, TableSlotsProperty) is { } slots
                ? (int)Math.Min(slots, (uint)int.MaxValue)
                : database.TableSlotCount;
            return;
        }
    }

    private static ulong? ReadRawRecordId(Dictionary<string, object?> properties)
    {
        var raw = ReadBase64(properties, RawRecordProperty);
        if (raw is null || raw.Length < 20)
        {
            return null;
        }

        var id = ReadUInt64(raw, 12);
        return id == 0 ? null : id;
    }

    private static byte[]? ReadBase64(Dictionary<string, object?> properties, string key)
    {
        var text = ReadString(properties, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new DedModelException($"row property '{key}' is not valid base64.");
        }
    }

    private static string? ReadString(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            var text = raw switch
            {
                JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
                JsonElement json => json.GetRawText(),
                _ => Convert.ToString(raw, CultureInfo.InvariantCulture)
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static ulong? ReadUlong(Dictionary<string, object?> row, params string[] keys)
    {
        var text = ReadString(row, keys);
        return text is not null && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static uint? ReadUint(Dictionary<string, object?> row, string key)
    {
        var value = ReadUlong(row, key);
        return value is <= uint.MaxValue ? (uint)value : null;
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24));

    private static ulong ReadUInt64(byte[] buffer, int offset) =>
        ReadUInt32(buffer, offset) | ((ulong)ReadUInt32(buffer, offset + 4) << 32);

    private sealed class PendingRow(
        string key,
        Dictionary<string, object?> row,
        Dictionary<string, object?> properties,
        string? parentKey)
    {
        public string Key { get; } = key;

        public Dictionary<string, object?> Row { get; } = row;

        public Dictionary<string, object?> Properties { get; } = properties;

        public string? ParentKey { get; } = parentKey;

        public ulong? ObjectId { get; set; }

        public DedCatalogObject? Created { get; set; }
    }
}
