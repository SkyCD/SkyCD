using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UCanAccess.File;
using SkyCD.Plugin.FileFormat.MyCatalogue.Exceptions;

namespace SkyCD.Plugin.FileFormat.MyCatalogue.Parsing;

internal static class MyCatalogueReader
{
    private const int MaxItems = 2_000_000;
    private const int MaxDisksAndGroups = 100_000;

    private static readonly (string Name, string[] Columns)[] Schema =
    [
        ("Disks", ["ID_Disk", "Group_ID", "DiskName", "Directories", "Files", "Bytes", "Serial", "Type", "DriveNumber"]),
        ("Groups", ["ID_Group", "Group_ID", "GroupName"]),
        ("Items", ["ID_Item", "Disk_ID", "ItemName", "IsDirectory", "Bytes", "CRC32", "IsCRC32Calculated", "Item_ID", "DateCreated", "DateModified"]),
        ("Info", ["ID_Info", "VersionMajor", "VersionMinor", "LastSearchText", "LastCatalogueExt"])
    ];

    public static void Validate(string path)
    {
        using var database = Open(path);
        ValidateSchemaAndVersion(database, CancellationToken.None);
    }

    public static IReadOnlyList<Dictionary<string, object?>> Read(string path, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var database = Open(path);
        var info = ValidateSchemaAndVersion(database, cancellationToken);
        var groups = new Dictionary<int, Group>();
        foreach (var row in ReadTable(database, "Groups", MaxDisksAndGroups, cancellationToken))
        {
            var group = new Group(row.Id("ID_Group"), row.ParentId("Group_ID"), row.RequiredText("GroupName"));
            if (!groups.TryAdd(group.Id, group)) throw Invalid("duplicate group ID.");
        }

        var groupParents = groups.ToDictionary(pair => pair.Key, pair => pair.Value.ParentId);
        ValidateParents(groupParents, "group", cancellationToken);
        progress?.Report(25);

        var disks = new Dictionary<int, Disk>();
        foreach (var row in ReadTable(database, "Disks", MaxDisksAndGroups, cancellationToken))
        {
            var id = row.Id("ID_Disk");
            var groupId = row.ParentId("Group_ID");
            if (groupId != 0 && !groups.ContainsKey(groupId)) throw Invalid("a disk references a missing group.");
            var properties = new Dictionary<string, object?>
            {
                ["SourceTable"] = "Disks",
                ["SourceId"] = id,
                ["MyCatalogueVersion"] = "1.2",
                ["MyCatalogueGroupId"] = groupId,
                ["SizeKnown"] = false
            };
            if (groupId != 0) properties["MyCatalogueGroupName"] = groups[groupId].Name;
            Add(properties, "SerialNumber", row.OptionalScalar("Serial"));
            Add(properties, "MyCatalogueDirectories", row.OptionalNonNegative("Directories"));
            Add(properties, "MyCatalogueFiles", row.OptionalNonNegative("Files"));
            Add(properties, "MyCatalogueBytes", row.OptionalNonNegative("Bytes"));
            Add(properties, "MyCatalogueDiskType", row.OptionalScalar("Type"));
            Add(properties, "MyCatalogueDriveNumber", row.OptionalScalar("DriveNumber"));
            Add(properties, "MyCatalogueLastCatalogueExt", info.OptionalText("LastCatalogueExt"));
            if (!disks.TryAdd(id, new Disk(id, groupId, row.RequiredText("DiskName"), properties)))
                throw Invalid("duplicate disk ID.");
        }
        progress?.Report(40);

        var items = new Dictionary<int, Item>();
        foreach (var row in ReadTable(database, "Items", MaxItems, cancellationToken))
        {
            var id = row.Id("ID_Item");
            var diskId = row.Id("Disk_ID");
            if (!disks.ContainsKey(diskId)) throw Invalid("an item references a missing disk.");
            var parentId = row.ParentId("Item_ID");
            var isDirectory = row.RequiredBoolean("IsDirectory");
            var size = row.OptionalNonNegative("Bytes");
            var properties = new Dictionary<string, object?>
            {
                ["SourceTable"] = "Items",
                ["SourceId"] = id,
                ["MyCatalogueDiskId"] = diskId,
                ["SizeKnown"] = size.HasValue
            };
            AddDate(properties, "DateCreated", row.OptionalDate("DateCreated"));
            AddDate(properties, "DateModified", row.OptionalDate("DateModified"));
            var crcCalculated = row.OptionalBoolean("IsCRC32Calculated");
            Add(properties, "MyCatalogueCrc32Calculated", crcCalculated);
            if (crcCalculated == true) properties["CRC32"] = row.Crc32("CRC32").ToString("X8", CultureInfo.InvariantCulture);
            var item = new Item(id, diskId, parentId, row.RequiredText("ItemName"), isDirectory, size, properties);
            if (!items.TryAdd(id, item)) throw Invalid("duplicate item ID.");
        }
        progress?.Report(65);

        var itemParents = new Dictionary<int, int>(items.Count);
        foreach (var item in items.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.ParentId != 0)
            {
                if (!items.TryGetValue(item.ParentId, out var parent)) throw Invalid("an item references a missing parent.");
                if (!parent.IsDirectory || parent.DiskId != item.DiskId)
                    throw Invalid("an item parent is not a directory on the same disk.");
            }
            itemParents.Add(item.Id, item.ParentId);
        }
        ValidateParents(itemParents, "item", cancellationToken);

        var result = new List<Dictionary<string, object?>>(groups.Count + disks.Count + items.Count);
        foreach (var group in groups.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var properties = new Dictionary<string, object?>
            {
                ["SourceTable"] = "Groups",
                ["SourceId"] = group.Id,
                ["MyCatalogueGroup"] = true,
                ["SizeKnown"] = false
            };
            result.Add(Node(GroupId(group.Id), group.ParentId == 0 ? null : GroupId(group.ParentId),
                "folder", group.Name, null, properties));
        }
        foreach (var disk in disks.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(Node(DiskId(disk.Id), disk.GroupId == 0 ? null : GroupId(disk.GroupId),
                "media", disk.Name, null, disk.Properties));
        }
        foreach (var item in items.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(Node(ItemId(item.Id), item.ParentId == 0 ? DiskId(item.DiskId) : ItemId(item.ParentId),
                item.IsDirectory ? "folder" : "file", item.Name, item.Size, item.Properties));
        }
        progress?.Report(95);
        return result;
    }

    private static Database Open(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            Span<byte> header = stackalloc byte[21];
            if (stream.Read(header) != header.Length || !header[..20].SequenceEqual("\0\u0001\0\0Standard Jet DB\0"u8) || header[20] != 1)
                throw Invalid("expected a Jet 4 MDB database.");
        }
        return Database.Open(path, readOnly: true, allowExternalLinks: false, openExclusive: false);
    }

    private static Row ValidateSchemaAndVersion(Database database, CancellationToken cancellationToken)
    {
        var metadata = database.GetTableMetaData();
        foreach (var (name, requiredColumns) in Schema)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = metadata.FirstOrDefault(item => !item.IsSystem && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.IsLinked) throw Invalid($"required local table '{name}' is missing.");
            var table = database.GetTable(name) ?? throw Invalid($"required table '{name}' is missing.");
            var columns = table.Columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var column in requiredColumns)
                if (!columns.Contains(column)) throw Invalid($"required column '{name}.{column}' is missing.");
        }
        var info = ReadTable(database, "Info", 1, cancellationToken).SingleOrDefault()
                   ?? throw Invalid("the catalog version is missing.");
        _ = info.Id("ID_Info");
        if (info.RequiredInteger("VersionMajor") != 1 || info.RequiredInteger("VersionMinor") != 2)
            throw Invalid("only MyCatalogue schema version 1.2 is supported.");
        return info;
    }

    private static IEnumerable<Row> ReadTable(Database database, string name, int limit, CancellationToken cancellationToken)
    {
        var table = database.GetTable(name) ?? throw Invalid($"required table '{name}' is missing.");
        var columns = table.Columns.Select(column => column.Name).ToArray();
        var count = 0;
        foreach (var values in table.Rows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > limit) throw Invalid($"table '{name}' exceeds the supported row limit.");
            var data = values.ToArray();
            if (data.Length != columns.Length) throw Invalid($"table '{name}' has a malformed row.");
            yield return new Row(name, columns, data);
        }
    }

    private static void ValidateParents(IReadOnlyDictionary<int, int> parents, string description, CancellationToken cancellationToken)
    {
        var complete = new HashSet<int>();
        foreach (var id in parents.Keys)
        {
            if (complete.Contains(id)) continue;
            var path = new HashSet<int>();
            var current = id;
            while (current != 0 && !complete.Contains(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!parents.TryGetValue(current, out var parent)) throw Invalid($"a {description} references a missing parent.");
                if (!path.Add(current)) throw Invalid($"the {description} hierarchy contains a cycle.");
                current = parent;
            }
            complete.UnionWith(path);
        }
    }

    private static Dictionary<string, object?> Node(string id, string? parentId, string kind, string name,
        long? size, Dictionary<string, object?> properties)
    {
        var node = new Dictionary<string, object?>
        {
            ["nodeId"] = id,
            ["parentId"] = parentId,
            ["kind"] = kind,
            ["name"] = name,
            ["properties"] = properties
        };
        if (size.HasValue) node["sizeBytes"] = size.Value;
        return node;
    }

    private static void Add(Dictionary<string, object?> properties, string name, object? value)
    {
        if (value is not null) properties[name] = value;
    }

    private static void AddDate(Dictionary<string, object?> properties, string name, DateTime? value)
    {
        if (value.HasValue)
            properties[name] = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified).ToString("O", CultureInfo.InvariantCulture);
    }

    private static string GroupId(int id) => $"group:mycatalogue:{id}";
    private static string DiskId(int id) => $"media:mycatalogue:{id}";
    private static string ItemId(int id) => $"item:mycatalogue:{id}";
    private static MyCatalogueImportException Invalid(string reason) => new(reason);

    private sealed record Group(int Id, int ParentId, string Name);
    private sealed record Disk(int Id, int GroupId, string Name, Dictionary<string, object?> Properties);
    private sealed record Item(int Id, int DiskId, int ParentId, string Name, bool IsDirectory, long? Size,
        Dictionary<string, object?> Properties);

    private sealed class Row
    {
        private readonly string table;
        private readonly Dictionary<string, object?> values;

        public Row(string table, string[] columns, object?[] data)
        {
            this.table = table;
            values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++) values.Add(columns[i], data[i] is DBNull ? null : data[i]);
        }

        public int Id(string column)
        {
            var value = RequiredInteger(column);
            if (value <= 0 || value > int.MaxValue) throw Bad(column);
            return (int)value;
        }

        public int ParentId(string column)
        {
            var value = RequiredInteger(column);
            if (value < 0 || value > int.MaxValue) throw Bad(column);
            return (int)value;
        }

        public long RequiredInteger(string column) => OptionalInteger(column) ?? throw Bad(column);

        public long? OptionalNonNegative(string column)
        {
            var number = OptionalInteger(column);
            if (number < 0) throw Bad(column);
            return number;
        }

        private long? OptionalInteger(string column)
        {
            var value = values[column];
            if (value is null) return null;
            return value switch
            {
                byte b => b,
                short s => s,
                int i => i,
                long l => l,
                double d when double.IsFinite(d) && d == Math.Truncate(d) && d >= long.MinValue && d < 9223372036854775808d => (long)d,
                decimal m when m == decimal.Truncate(m) && m >= long.MinValue && m <= long.MaxValue => (long)m,
                _ => throw Bad(column)
            };
        }

        public string RequiredText(string column) => OptionalText(column) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value : throw Bad(column);

        public string? OptionalText(string column) => values[column] switch
        {
            null => null,
            string text => text,
            _ => throw Bad(column)
        };

        public object? OptionalScalar(string column) => values[column] switch
        {
            null => null,
            string or byte or short or int or long => values[column],
            _ => throw Bad(column)
        };

        public bool RequiredBoolean(string column) => OptionalBoolean(column) ?? throw Bad(column);

        public bool? OptionalBoolean(string column) => values[column] switch
        {
            null => null,
            bool value => value,
            _ => throw Bad(column)
        };

        public DateTime? OptionalDate(string column) => values[column] switch
        {
            null => null,
            DateTime value => value,
            _ => throw Bad(column)
        };

        public uint Crc32(string column)
        {
            var number = RequiredInteger(column);
            if (number < int.MinValue || number > uint.MaxValue) throw Bad(column);
            return unchecked((uint)number);
        }

        private MyCatalogueImportException Bad(string column) => Invalid($"invalid value in '{table}.{column}'.");
    }
}
