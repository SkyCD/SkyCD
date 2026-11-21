using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkyCD.Plugin.CdCatalogExpert.Exceptions;
using SkyCD.Plugin.CdCatalogExpert.Export;
using SkyCD.Plugin.CdCatalogExpert.Models;
using SkyCD.Plugin.CdCatalogExpert.Parsing;

namespace SkyCD.Plugin.CdCatalogExpert.Import;

/// <summary>
/// Converts between parsed ZDC catalogs and the shared flat node rows, and
/// between rows and containers.
/// </summary>
/// <remarks>
/// <para>
/// Row shapes follow the repository convention: <c>nodeId</c>,
/// <c>parentId</c>, <c>kind</c> (<c>folder</c>/<c>media</c>/<c>file</c>),
/// <c>name</c>, <c>sizeBytes</c>, and a <c>properties</c> dictionary. Raw
/// legacy values travel as their original invariant text; unknown XML
/// attributes travel as JSON arrays; covers travel as Base64.
/// </para>
/// <para>
/// Writing regenerates disc GUIDs deterministically from the catalog and
/// disc identity, so repeated saves of unchanged rows are byte-identical
/// apart from values the host explicitly changes.
/// </para>
/// </remarks>
public static class ZdcCatalogMapper
{
    internal const string NodeKindKey = "ZdcNodeKind";
    internal const string CatalogNameKey = "ZdcCatalogName";
    internal const string CatalogDescriptionKey = "ZdcCatalogDescription";
    internal const string CatalogExtensionsKey = "ZdcCatalogExtensions";
    internal const string GroupTimestampKey = "ZdcGroupTimestamp";
    internal const string GroupExtensionsKey = "ZdcGroupExtensions";
    internal const string DiscGuidKey = "ZdcDiscGuid";
    internal const string SourcePathKey = "ZdcSourcePath";
    internal const string CreatedKey = "ZdcCreated";
    internal const string ModifiedKey = "ZdcModified";
    internal const string DescriptionKey = "ZdcDescription";
    internal const string VolumeLabelKey = "ZdcVolumeLabel";
    internal const string FileSystemKey = "ZdcFileSystem";
    internal const string MediaTypeKey = "ZdcMediaType";
    internal const string VolumeSerialKey = "ZdcVolumeSerial";
    internal const string CapacityKey = "ZdcCapacity";
    internal const string FreeSpaceKey = "ZdcFreeSpace";
    internal const string OptionsKey = "ZdcOptions";
    internal const string ScanFlagAKey = "ZdcScanFlagA";
    internal const string ScanFlagBKey = "ZdcScanFlagB";
    internal const string ScanFlagCKey = "ZdcScanFlagC";
    internal const string ScanFlagDKey = "ZdcScanFlagD";
    internal const string ScanFlagEKey = "ZdcScanFlagE";
    internal const string ScanFlagFKey = "ZdcScanFlagF";
    internal const string ScanFlagGKey = "ZdcScanFlagG";
    internal const string CoverMemberKey = "ZdcCoverMember";
    internal const string CoverKey = "ZdcCover";
    internal const string KeywordsKey = "ZdcKeywords";
    internal const string StorageLocationKey = "ZdcStorageLocation";
    internal const string IndexCodeKey = "ZdcIndexCode";
    internal const string BarcodeKey = "ZdcBarcode";
    internal const string RatingKey = "ZdcRating";
    internal const string DiscExtensionsKey = "ZdcDiscExtensions";
    internal const string AttributesKey = "ZdcAttributes";
    internal const string TimestampBKey = "ZdcTimestampB";
    internal const string EntryExtensionsKey = "ZdcEntryExtensions";

    private const string SourceKey = "ZdcSource";
    private const string SourceValue = "cdcatalogexpert-native";
    private const string CscKey = "ZdcCsc";
    private const string OofKey = "ZdcOof";
    private const string ProfileKeyPrefix = "ZdcProfile";
    private const string ReservedProfileKey = "ZdcReservedProfile";

    /// <summary>Builds the flat row document for a parsed catalog.</summary>
    public static ZdcReportDocument BuildRows(ZdcCatalogReadResult readResult, ZdcDatabase database, string fileStem)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(database);

        var catalog = readResult.Catalog;
        var rows = new List<Dictionary<string, object?>>();
        var counts = new Counters();

        var catalogId = "zdc:catalog";
        var catalogProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [NodeKindKey] = "catalog",
            [SourceKey] = SourceValue,
            [CatalogNameKey] = catalog.Name,
            [CscKey] = database.Csc.ToString(CultureInfo.InvariantCulture),
            [OofKey] = database.Oof.ToString(CultureInfo.InvariantCulture),
            [ReservedProfileKey] = Convert.ToBase64String(database.ReservedProfile)
        };
        for (var i = 0; i < database.ProfileValues.Length; i++)
        {
            catalogProperties[$"{ProfileKeyPrefix}{(char)('A' + i)}"] =
                database.ProfileValues[i].ToString(CultureInfo.InvariantCulture);
        }

        SetIfNotEmpty(catalogProperties, CatalogDescriptionKey, catalog.Description);
        SetExtensions(catalogProperties, CatalogExtensionsKey, catalog.ExtensionAttributes);

        rows.Add(Row(catalogId, null, "folder", catalog.Name, "0", catalogProperties));
        AddIndexEntries(rows, catalog.Children, catalogId, "g", counts);
        counts.Records = counts.Groups + counts.Discs + counts.Entries;

        var skippedReasons = new List<string>(readResult.Warnings);
        WalkDiscs(catalog.Children, disc =>
        {
            if (disc.StorageLocation is not null && string.IsNullOrWhiteSpace(disc.StorageLocation))
            {
                skippedReasons.Add($"Disc '{disc.Name}': blank storage location value was ignored.");
            }
        });

        var summary = new ZdcImportSummary(
            TotalRecordsRead: counts.Records,
            ImportedRecordsCount: counts.Records,
            SkippedRecordsCount: 0,
            IsPartialReport: false,
            SkippedReasons: skippedReasons,
            UnsupportedFields: CollectUnsupportedMembers(database).ToList(),
            DetectedVersion: "9.x");

        _ = fileStem;
        return new ZdcReportDocument(rows, summary);
    }

    private static void AddIndexEntries(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ZdcCatalogEntry> entries,
        string parentId,
        string indexPath,
        Counters counts)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var path = $"{indexPath}/{i}";
            switch (entries[i])
            {
                case ZdcGroup group:
                    {
                        var id = $"zdc:group/{path}";
                        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            [NodeKindKey] = "group"
                        };
                        SetIfNotEmpty(properties, GroupTimestampKey, group.Timestamp);
                        SetExtensions(properties, GroupExtensionsKey, group.ExtensionAttributes);
                        rows.Add(Row(id, parentId, "folder", group.Name, "0", properties));
                        counts.Groups++;
                        AddIndexEntries(rows, group.Children, id, path, counts);
                        break;
                    }

                case ZdcDisc disc:
                    {
                        var id = $"zdc:media/{path}";
                        rows.Add(Row(id, parentId, "media", disc.Name, "0", BuildDiscProperties(disc)));
                        counts.Discs++;
                        AddTreeEntries(rows, disc.Tree.Entries, id, id, counts);
                        break;
                    }

                default:
                    throw new ZdcFormatException($"unsupported catalog entry '{entries[i].GetType().Name}'.");
            }
        }
    }

    private static Dictionary<string, object?> BuildDiscProperties(ZdcDisc disc)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [NodeKindKey] = "disc",
            [DiscGuidKey] = disc.GuidText
        };

        SetIfNotEmpty(properties, SourcePathKey, disc.SourcePath);
        SetIfNotEmpty(properties, CreatedKey, disc.Created);
        SetIfNotEmpty(properties, ModifiedKey, disc.Modified);
        SetIfNotEmpty(properties, DescriptionKey, disc.Description);
        SetIfNotEmpty(properties, VolumeLabelKey, disc.VolumeLabel);
        SetIfNotEmpty(properties, FileSystemKey, disc.FileSystem);
        SetIfNotEmpty(properties, MediaTypeKey, disc.MediaType);
        SetIfNotEmpty(properties, VolumeSerialKey, disc.VolumeSerial);
        SetIfNotEmpty(properties, CapacityKey, disc.Capacity);
        SetIfNotEmpty(properties, FreeSpaceKey, disc.FreeSpace);
        SetIfNotEmpty(properties, OptionsKey, disc.Options);
        SetIfNotEmpty(properties, ScanFlagAKey, disc.ScanFlagA);
        SetIfNotEmpty(properties, ScanFlagBKey, disc.ScanFlagB);
        SetIfNotEmpty(properties, ScanFlagCKey, disc.ScanFlagC);
        SetIfNotEmpty(properties, ScanFlagDKey, disc.ScanFlagD);
        SetIfNotEmpty(properties, ScanFlagEKey, disc.ScanFlagE);
        SetIfNotEmpty(properties, ScanFlagFKey, disc.ScanFlagF);
        SetIfNotEmpty(properties, ScanFlagGKey, disc.ScanFlagG);
        SetIfNotEmpty(properties, CoverMemberKey, disc.CoverMemberName);
        SetIfNotEmpty(properties, KeywordsKey, disc.Keywords);
        SetIfNotEmpty(properties, StorageLocationKey, disc.StorageLocation);
        SetIfNotEmpty(properties, IndexCodeKey, disc.IndexCode);
        SetIfNotEmpty(properties, BarcodeKey, disc.Barcode);
        SetIfNotEmpty(properties, RatingKey, disc.Rating);
        if (disc.Cover is { Length: > 0 })
        {
            properties[CoverKey] = Convert.ToBase64String(disc.Cover);
        }

        SetExtensions(properties, DiscExtensionsKey, disc.ExtensionAttributes);
        return properties;
    }

    private static void AddTreeEntries(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<ZdcTreeEntry> entries,
        string parentId,
        string pathBase,
        Counters counts)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var path = $"{pathBase}/{i}";
            var kind = entry.IsDirectory ? "folder" : "file";
            var id = $"zdc:tree/{path}";
            var size = entry.IsDirectory ? "0" : entry.Size ?? "0";
            rows.Add(Row(id, parentId, kind, entry.Name, size, BuildTreeProperties(entry)));
            counts.Entries++;
            if (entry.IsDirectory)
            {
                AddTreeEntries(rows, entry.Children, id, path, counts);
            }
        }
    }

    private static Dictionary<string, object?> BuildTreeProperties(ZdcTreeEntry entry)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [NodeKindKey] = entry.IsDirectory ? "directory" : "file"
        };
        SetIfNotEmpty(properties, AttributesKey, entry.Attributes);
        SetIfNotEmpty(properties, ModifiedKey, entry.Modified);
        SetIfNotEmpty(properties, CreatedKey, entry.Created);
        SetIfNotEmpty(properties, TimestampBKey, entry.TimestampB);
        SetIfNotEmpty(properties, DescriptionKey, entry.Description);
        SetIfNotEmpty(properties, KeywordsKey, entry.Keywords);
        SetExtensions(properties, EntryExtensionsKey, entry.ExtensionAttributes);
        return properties;
    }

    /// <summary>Rebuilds a container from the flat rows.</summary>
    public static ZdcDatabase ToDatabase(IReadOnlyList<Dictionary<string, object?>> rows, string? fileStem)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var node = RowTree.Build(rows);
        var catalogRow = node.Roots.FirstOrDefault(r =>
            string.Equals(ReadString(r.Properties, NodeKindKey), "catalog", StringComparison.OrdinalIgnoreCase));

        var catalog = new ZdcCatalog();
        var children = catalogRow is null ? node.Roots : catalogRow.Children;
        if (catalogRow is not null)
        {
            catalog.Name = ReadString(catalogRow.Properties, CatalogNameKey) is { Length: > 0 } name
                ? name
                : catalogRow.Name;
            catalog.Description = ReadString(catalogRow.Properties, CatalogDescriptionKey) ?? string.Empty;
            ReadExtensions(catalogRow.Properties, CatalogExtensionsKey, catalog.ExtensionAttributes);
        }
        else
        {
            catalog.Name = string.IsNullOrWhiteSpace(fileStem) ? "Catalog" : fileStem.Trim();
        }

        var writer = new IndexBuilder(catalog.Name);
        writer.AddEntries(children);
        catalog.Children.AddRange(writer.Entries);

        var database = new ZdcDatabase();
        if (catalogRow is not null)
        {
            var properties = catalogRow.Properties;
            database.Csc = ParseUInt32(properties, CscKey);
            database.Oof = ParseUInt32(properties, OofKey);
            for (var i = 0; i < database.ProfileValues.Length; i++)
            {
                database.ProfileValues[i] = ParseUInt32(properties, $"{ProfileKeyPrefix}{(char)('A' + i)}");
            }

            database.ReservedProfile = ParseBytes(properties, ReservedProfileKey, ZdcFormat.ReservedProfileSize);
        }

        AddMembers(database, catalog);
        return database;
    }

    private static void AddMembers(ZdcDatabase database, ZdcCatalog catalog)
    {
        database.Members.Add(new ZdcMember
        {
            FileName = ZdcFormat.IndexMemberName,
            Payload = ZdcCatalogXmlWriter.WriteIndex(catalog)
        });

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ZdcFormat.IndexMemberName
        };
        var discIndex = 0;
        WalkDiscs(catalog.Children, disc =>
        {
            var guidText = NormalizeGuid(disc.GuidText, catalog.Name, disc.Name, discIndex);
            while (!usedNames.Add($"{guidText}.xml"))
            {
                guidText = NormalizeGuid(null, catalog.Name, disc.Name, discIndex + usedNames.Count);
            }

            discIndex++;
            disc.GuidText = guidText;
            database.Members.Add(new ZdcMember
            {
                FileName = $"{guidText}.xml",
                Payload = ZdcCatalogXmlWriter.WriteDiscTree(disc.Tree)
            });

            if (disc.Cover is { Length: > 0 })
            {
                var coverName = string.IsNullOrWhiteSpace(disc.CoverMemberName)
                    ? $"{guidText}.jpg"
                    : disc.CoverMemberName;
                if (!usedNames.Add(coverName))
                {
                    coverName = $"{guidText}.jpg";
                    usedNames.Add(coverName);
                }

                disc.CoverMemberName = coverName;
                database.Members.Add(new ZdcMember
                {
                    FileName = coverName,
                    Payload = disc.Cover
                });
            }
        });
    }

    internal static string NormalizeGuid(string? guidText, string catalogName, string discName, int index)
    {
        var trimmed = guidText?.Trim() ?? string.Empty;
        var bare = trimmed.StartsWith('{') && trimmed.EndsWith('}') ? trimmed[1..^1] : trimmed;
        if (Guid.TryParse(bare, out var parsed))
        {
            return $"{{{parsed:D}}}";
        }

        var seed = Encoding.UTF8.GetBytes(
            $"{catalogName}\u001F{discName}\u001F{index.ToString(CultureInfo.InvariantCulture)}");
        var hash = SHA256.HashData(seed);
        var generated = new Guid(hash.AsSpan(0, 16));
        return $"{{{generated:D}}}";
    }

    private static IEnumerable<string> CollectUnsupportedMembers(ZdcDatabase database)
    {
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var member in database.Members)
        {
            if (member.FileType != 0)
            {
                unsupported.Add($"FileType=0x{member.FileType:X8}");
            }

            if (member.EntryType != 0)
            {
                unsupported.Add($"EntryType=0x{member.EntryType:X8}");
            }
        }

        return unsupported;
    }

    private static Dictionary<string, object?> Row(
        string nodeId,
        string? parentId,
        string kind,
        string name,
        string sizeBytes,
        Dictionary<string, object?> properties) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = kind,
            ["name"] = name,
            ["sizeBytes"] = sizeBytes,
            ["properties"] = properties
        };

    private static void SetIfNotEmpty(Dictionary<string, object?> properties, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            properties[key] = value;
        }
    }

    private static void SetExtensions(
        Dictionary<string, object?> properties,
        string key,
        List<KeyValuePair<string, string>> extensions)
    {
        if (extensions.Count > 0)
        {
            properties[key] = JsonSerializer.Serialize(extensions);
        }
    }

    private static void ReadExtensions(
        IReadOnlyDictionary<string, object?> properties,
        string key,
        List<KeyValuePair<string, string>> target)
    {
        var json = ReadString(properties, key);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(json);
            if (parsed is not null)
            {
                target.AddRange(parsed);
            }
        }
        catch (JsonException)
        {
            // Leniency on the write path: unreadable extension data is dropped.
        }
    }

    private static uint ParseUInt32(IReadOnlyDictionary<string, object?> properties, string key) =>
        uint.TryParse(ReadString(properties, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static byte[] ParseBytes(
        IReadOnlyDictionary<string, object?> properties,
        string key,
        int expectedLength)
    {
        var text = ReadString(properties, key);
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                var raw = Convert.FromBase64String(text);
                if (raw.Length == expectedLength || expectedLength == 0)
                {
                    return raw;
                }
            }
            catch (FormatException)
            {
                // Leniency on the write path: unreadable preserved values are dropped.
            }
        }

        return new byte[expectedLength];
    }

    internal static string? ReadString(IReadOnlyDictionary<string, object?> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static void WalkDiscs(IEnumerable<ZdcCatalogEntry> entries, Action<ZdcDisc> visit)
    {
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case ZdcGroup group:
                    WalkDiscs(group.Children, visit);
                    break;
                case ZdcDisc disc:
                    visit(disc);
                    break;
                default:
                    throw new ZdcFormatException($"unsupported catalog entry '{entry.GetType().Name}'.");
            }
        }
    }

    private sealed class Counters
    {
        public int Groups;
        public int Discs;
        public int Entries;
        public int Records;
    }

    private sealed class RowTree
    {
        private readonly Dictionary<string, RowTree> byId = new(StringComparer.Ordinal);

        private RowTree(Dictionary<string, object?> row, string id)
        {
            Row = row;
            Id = id;
        }

        public Dictionary<string, object?> Row { get; }

        public string Id { get; }

        public string Name => Row.TryGetValue("name", out var name) ? name?.ToString() ?? string.Empty : string.Empty;

        public IReadOnlyDictionary<string, object?> Properties => PropertyReader.Read(Row);

        public List<RowTree> Children { get; } = [];

        public List<RowTree> Roots { get; } = [];

        public static RowTree Build(IReadOnlyList<Dictionary<string, object?>> rows)
        {
            var root = new RowTree(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), "<root>");
            var all = new List<RowTree>(rows.Count);
            foreach (var row in rows)
            {
                var id = ReadString(row, "nodeId") ?? ReadString(row, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = $"row:{all.Count}";
                }

                if (root.byId.ContainsKey(id))
                {
                    id = $"{id}#{all.Count}";
                }

                var node = new RowTree(row, id);
                root.byId[id] = node;
                all.Add(node);
            }

            foreach (var node in all)
            {
                var parentId = ReadString(node.Row, "parentId") ?? ReadString(node.Row, "parent");
                if (string.IsNullOrWhiteSpace(parentId) ||
                    parentId.Equals("-1", StringComparison.Ordinal) ||
                    !root.byId.TryGetValue(parentId, out var parent))
                {
                    root.Roots.Add(node);
                    continue;
                }

                parent.Children.Add(node);
            }

            // Break parent cycles: any node that never reaches a root is promoted.
            var reachable = new HashSet<RowTree>();
            var stack = new Stack<RowTree>(root.Roots);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!reachable.Add(current))
                {
                    continue;
                }

                foreach (var child in current.Children)
                {
                    stack.Push(child);
                }
            }

            foreach (var node in all)
            {
                if (!reachable.Contains(node))
                {
                    root.Roots.Add(node);
                }
            }

            return root;
        }
    }

    private sealed class IndexBuilder(string catalogName)
    {
        public List<ZdcCatalogEntry> Entries { get; } = [];

        public void AddEntries(IReadOnlyList<RowTree> rows)
        {
            foreach (var row in rows)
            {
                var kind = KindOf(row);
                if (kind == "media")
                {
                    Entries.Add(BuildDisc(row, catalogName));
                    continue;
                }

                if (kind == "folder" && string.Equals(ReadString(row.Properties, NodeKindKey), "group", StringComparison.OrdinalIgnoreCase))
                {
                    Entries.Add(BuildGroup(row));
                    continue;
                }

                if (kind == "folder" && ContainsMedia(row))
                {
                    Entries.Add(BuildGroup(row));
                    continue;
                }

                var disc = BuildDiscFromSubtree(row, catalogName);
                if (disc is not null)
                {
                    Entries.Add(disc);
                }
            }
        }

        private ZdcGroup BuildGroup(RowTree row)
        {
            var group = new ZdcGroup
            {
                Name = row.Name,
                Timestamp = ReadString(row.Properties, GroupTimestampKey)
            };
            ReadExtensions(row.Properties, GroupExtensionsKey, group.ExtensionAttributes);
            var builder = new IndexBuilder(catalogName);
            builder.AddEntries(row.Children);
            group.Children.AddRange(builder.Entries);
            return group;
        }

        private ZdcDisc? BuildDiscFromSubtree(RowTree row, string catalogName)
        {
            var kind = KindOf(row);
            if (kind is "file")
            {
                var synthetic = new ZdcDisc { Name = catalogName };
                AddTreeRows(synthetic.Tree, [row]);
                return synthetic;
            }

            var disc = new ZdcDisc { Name = row.Name };
            ReadDiscProperties(disc, row.Properties);
            AddTreeRows(disc.Tree, row.Children);
            return disc;
        }

        private void AddTreeRows(ZdcDiscTree tree, IReadOnlyList<RowTree> rows) =>
            tree.Entries.AddRange(rows.Select(ToTreeEntry));

        private static ZdcTreeEntry ToTreeEntry(RowTree row)
        {
            var isDirectory = !string.Equals(KindOf(row), "file", StringComparison.OrdinalIgnoreCase);
            var entry = new ZdcTreeEntry
            {
                IsDirectory = isDirectory,
                Name = row.Name,
                Attributes = ReadString(row.Properties, AttributesKey),
                Modified = ReadString(row.Properties, ModifiedKey),
                Created = ReadString(row.Properties, CreatedKey),
                TimestampB = ReadString(row.Properties, TimestampBKey),
                Description = ReadString(row.Properties, DescriptionKey),
                Keywords = ReadString(row.Properties, KeywordsKey),
                Size = isDirectory ? null : ReadSize(row)
            };
            ReadExtensions(row.Properties, EntryExtensionsKey, entry.ExtensionAttributes);
            foreach (var child in row.Children)
            {
                entry.Children.Add(ToTreeEntry(child));
            }

            return entry;
        }

        private static string ReadSize(RowTree row)
        {
            var size = row.Row.TryGetValue("sizeBytes", out var value) ? value?.ToString() : null;
            if (string.IsNullOrWhiteSpace(size))
            {
                size = row.Row.TryGetValue("size", out var legacy) ? legacy?.ToString() : null;
            }

            return ulong.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                ? size!
                : "0";
        }

        private static bool ContainsMedia(RowTree row) =>
            row.Children.Any(child =>
                KindOf(child) == "media" || ContainsMedia(child));

        private static string KindOf(RowTree row) =>
            row.Row.TryGetValue("kind", out var kind) ? kind?.ToString()?.ToLowerInvariant() ?? string.Empty : string.Empty;
    }

    private static ZdcDisc BuildDisc(RowTree row, string catalogName)
    {
        var disc = new ZdcDisc { Name = row.Name };
        ReadDiscProperties(disc, row.Properties);
        foreach (var child in row.Children)
        {
            disc.Tree.Entries.Add(IndexBuilderToTreeEntry(child));
        }

        _ = catalogName;
        return disc;
    }

    private static void ReadDiscProperties(
        ZdcDisc disc,
        IReadOnlyDictionary<string, object?> properties)
    {
        disc.GuidText = ReadString(properties, DiscGuidKey) ?? string.Empty;
        disc.SourcePath = ReadString(properties, SourcePathKey);
        disc.Created = ReadString(properties, CreatedKey);
        disc.Modified = ReadString(properties, ModifiedKey);
        disc.Description = ReadString(properties, DescriptionKey);
        disc.VolumeLabel = ReadString(properties, VolumeLabelKey);
        disc.FileSystem = ReadString(properties, FileSystemKey);
        disc.MediaType = ReadString(properties, MediaTypeKey);
        disc.VolumeSerial = ReadString(properties, VolumeSerialKey);
        disc.Capacity = ReadString(properties, CapacityKey);
        disc.FreeSpace = ReadString(properties, FreeSpaceKey);
        disc.Options = ReadString(properties, OptionsKey);
        disc.ScanFlagA = ReadString(properties, ScanFlagAKey);
        disc.ScanFlagB = ReadString(properties, ScanFlagBKey);
        disc.ScanFlagC = ReadString(properties, ScanFlagCKey);
        disc.ScanFlagD = ReadString(properties, ScanFlagDKey);
        disc.ScanFlagE = ReadString(properties, ScanFlagEKey);
        disc.ScanFlagF = ReadString(properties, ScanFlagFKey);
        disc.ScanFlagG = ReadString(properties, ScanFlagGKey);
        disc.CoverMemberName = ReadString(properties, CoverMemberKey);
        disc.Keywords = ReadString(properties, KeywordsKey);
        disc.StorageLocation = ReadString(properties, StorageLocationKey);
        disc.IndexCode = ReadString(properties, IndexCodeKey);
        disc.Barcode = ReadString(properties, BarcodeKey);
        disc.Rating = ReadString(properties, RatingKey);
        ReadExtensions(properties, DiscExtensionsKey, disc.ExtensionAttributes);
        var cover = ReadString(properties, CoverKey);
        if (!string.IsNullOrEmpty(cover))
        {
            try
            {
                disc.Cover = Convert.FromBase64String(cover);
            }
            catch (FormatException)
            {
                disc.Cover = null;
            }
        }
    }

    private static ZdcTreeEntry IndexBuilderToTreeEntry(RowTree row)
    {
        var isDirectory = !string.Equals(
            row.Row.TryGetValue("kind", out var kind) ? kind?.ToString() : null,
            "file",
            StringComparison.OrdinalIgnoreCase);
        var entry = new ZdcTreeEntry
        {
            IsDirectory = isDirectory,
            Name = row.Name,
            Attributes = ReadString(row.Properties, AttributesKey),
            Modified = ReadString(row.Properties, ModifiedKey),
            Created = ReadString(row.Properties, CreatedKey),
            TimestampB = ReadString(row.Properties, TimestampBKey),
            Description = ReadString(row.Properties, DescriptionKey),
            Keywords = ReadString(row.Properties, KeywordsKey),
            Size = isDirectory ? null : ReadSize(row)
        };
        ReadExtensions(row.Properties, EntryExtensionsKey, entry.ExtensionAttributes);
        foreach (var child in row.Children)
        {
            entry.Children.Add(IndexBuilderToTreeEntry(child));
        }

        return entry;
    }

    private static string ReadSize(RowTree row)
    {
        var size = row.Row.TryGetValue("sizeBytes", out var value) ? value?.ToString() : null;
        if (string.IsNullOrWhiteSpace(size))
        {
            size = row.Row.TryGetValue("size", out var legacy) ? legacy?.ToString() : null;
        }

        return ulong.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? size! : "0";
    }

    private static class PropertyReader
    {
        public static IReadOnlyDictionary<string, object?> Read(Dictionary<string, object?> row)
        {
            if (row.TryGetValue("properties", out var raw) && raw is IReadOnlyDictionary<string, object?> dictionary)
            {
                return dictionary;
            }

            if (row.TryGetValue("Properties", out raw) && raw is IReadOnlyDictionary<string, object?> upper)
            {
                return upper;
            }

            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ReadString(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.ToString() : null;
}
