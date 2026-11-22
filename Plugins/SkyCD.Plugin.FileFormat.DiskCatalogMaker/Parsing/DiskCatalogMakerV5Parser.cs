using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

/// <summary>
/// Parses the DiskCatalogMaker v5 binary container and its columnar tables.
/// Every offset and length is validated against the payload before use.
/// </summary>
public static class DiskCatalogMakerV5Parser
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Parses a complete v5 catalog payload (the contents of Catalog.dcm or a .dcmf file).
    /// </summary>
    public static DiskCatalogMakerV5Catalog Parse(byte[] payload, string? sourceDescription = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var source = string.IsNullOrWhiteSpace(sourceDescription) ? "catalog" : sourceDescription;
        if (payload.Length < DiskCatalogMakerV5Format.HeaderSize)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"the payload is {payload.Length} byte(s), shorter than the {DiskCatalogMakerV5Format.HeaderSize}-byte v5 header ({source}).");
        }

        var reader = new DiskCatalogMakerV5BinaryReader(payload, source);
        var header = ReadHeader(reader, source);
        var entries = ReadDirectory(reader, header.DirectoryOffset, header.TableCount, source);
        ValidateRanges(
            entries,
            ReadNonNegativeInt(header.DirectoryOffset, "table directory offset", source),
            (int)header.TableCount);
        return ReadTables(reader, entries, header.Revision);
    }

    private static Header ReadHeader(DiskCatalogMakerV5BinaryReader reader, string source)
    {
        var magic = reader.ReadSpan(DiskCatalogMakerV5Format.Magic.Length, "catalog magic");
        if (!magic.SequenceEqual(DiskCatalogMakerV5Format.Magic))
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"invalid magic at offset 0 in '{source}'; expected 'DCM5'.");
        }

        var headerSize = reader.ReadUInt16("header size");
        if (headerSize != DiskCatalogMakerV5Format.HeaderSize)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"unsupported header size {headerSize} at offset 4 in '{source}'; expected {DiskCatalogMakerV5Format.HeaderSize}.");
        }

        var majorVersion = reader.ReadUInt16("major version");
        var minorVersion = reader.ReadUInt16("minor version");
        if (majorVersion != DiskCatalogMakerV5Format.MajorVersion)
        {
            throw new DiskCatalogMakerV5UnsupportedVersionException(majorVersion, minorVersion);
        }

        var endianMarker = reader.ReadUInt16("byte-order marker");
        if (endianMarker != DiskCatalogMakerV5Format.LittleEndianMarker)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"unsupported byte-order marker 0x{endianMarker:X4} at offset 10 in '{source}'; only little-endian v5 catalogs are supported.");
        }

        var revision = reader.ReadUInt32("catalog revision");
        var directoryOffset = reader.ReadUInt32("table directory offset");
        var tableCount = reader.ReadUInt32("table count");
        reader.Skip(sizeof(uint) * 2, "header flags and checksum");
        return new Header(revision, directoryOffset, tableCount);
    }

    private static List<DirectoryEntry> ReadDirectory(
        DiskCatalogMakerV5BinaryReader reader,
        uint directoryOffsetRaw,
        uint tableCountRaw,
        string source)
    {
        var directoryOffset = ReadNonNegativeInt(directoryOffsetRaw, "table directory offset", source);
        if (tableCountRaw > DiskCatalogMakerV5Format.MaximumTableCount)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"table count {tableCountRaw} exceeds the supported maximum of {DiskCatalogMakerV5Format.MaximumTableCount} ({source}).");
        }

        var tableCount = (int)tableCountRaw;
        var directoryLength = tableCount * DiskCatalogMakerV5Format.DirectoryEntrySize;
        if (directoryOffset < DiskCatalogMakerV5Format.HeaderSize)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"table directory offset {directoryOffset} points inside the catalog header ({source}).");
        }

        reader.EnsureAt(directoryOffset, directoryLength, "table directory");
        reader.Seek(directoryOffset, "table directory");

        var entries = new List<DirectoryEntry>(tableCount);
        var identifiers = new HashSet<uint>();
        for (var index = 0; index < tableCount; index++)
        {
            var tableId = reader.ReadUInt32($"table directory entry {index} identifier");
            var offset = ReadNonNegativeInt(reader.ReadUInt32($"table directory entry {index} offset"), $"table directory entry {index} offset", source);
            var length = ReadNonNegativeInt(reader.ReadUInt32($"table directory entry {index} length"), $"table directory entry {index} length", source);
            var recordCount = reader.ReadUInt32($"table directory entry {index} record count");
            var flags = reader.ReadUInt32($"table directory entry {index} flags");

            if (!identifiers.Add(tableId))
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table identifier 0x{tableId:X} appears more than once in the table directory ({source}).");
            }

            if (length < DiskCatalogMakerV5Format.TableHeaderSize)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table 0x{tableId:X} declares a length of {length} byte(s), shorter than its {DiskCatalogMakerV5Format.TableHeaderSize}-byte header ({source}).");
            }

            if (recordCount > DiskCatalogMakerV5Format.MaximumRecordCount)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table 0x{tableId:X} declares {recordCount} records, exceeding the supported maximum of {DiskCatalogMakerV5Format.MaximumRecordCount} ({source}).");
            }

            reader.EnsureAt(offset, length, $"table 0x{tableId:X}");
            entries.Add(new DirectoryEntry(tableId, offset, length, recordCount, flags));
        }

        return entries;
    }

    private static void ValidateRanges(List<DirectoryEntry> entries, int directoryOffset, int tableCount)
    {
        var directoryLength = tableCount * DiskCatalogMakerV5Format.DirectoryEntrySize;
        var ranges = new List<(long Start, long End, string Label)>
        {
            (0, DiskCatalogMakerV5Format.HeaderSize, "catalog header"),
            (directoryOffset, (long)directoryOffset + directoryLength, "table directory")
        };

        ranges.AddRange(entries.Select(entry =>
            ((long)entry.Offset, (long)entry.Offset + entry.Length,
                $"table 0x{entry.TableId:X} (offset {entry.Offset}, length {entry.Length})")));

        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        for (var index = 1; index < ranges.Count; index++)
        {
            var previous = ranges[index - 1];
            var current = ranges[index];
            if (current.Start < previous.End)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"{previous.Label} and {current.Label} overlap; table ranges must not overlap.");
            }
        }
    }

    private static DiskCatalogMakerV5Catalog ReadTables(
        DiskCatalogMakerV5BinaryReader reader,
        List<DirectoryEntry> entries,
        uint revision)
    {
        var warnings = new WarningCollector();
        var strings = new Dictionary<uint, string>();
        var volumes = new List<VolumeRecord>();
        var items = new List<ItemRecord>();
        var aliases = new List<RawAliasRecord>();
        var thumbnails = new List<ThumbnailRecord>();
        var unknownTables = new List<DiskCatalogMakerV5UnknownTable>();
        var tableInfos = new List<DiskCatalogMakerV5TableInfo>(entries.Count);

        foreach (var entry in entries)
        {
            reader.Seek(entry.Offset, $"table 0x{entry.TableId:X} header");
            var headerTableId = reader.ReadUInt32($"table 0x{entry.TableId:X} header identifier");
            var headerValue = reader.ReadUInt32($"table 0x{entry.TableId:X} header value");
            var headerRecordCount = reader.ReadUInt32($"table 0x{entry.TableId:X} header record count");
            var payloadLength = reader.ReadUInt32($"table 0x{entry.TableId:X} header payload length");

            if (headerTableId != entry.TableId)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table header identifier 0x{headerTableId:X} does not match directory identifier 0x{entry.TableId:X} at offset {entry.Offset}.");
            }

            if (headerRecordCount != entry.RecordCount)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table 0x{entry.TableId:X} header record count {headerRecordCount} does not match directory count {entry.RecordCount} at offset {entry.Offset}.");
            }

            var expectedPayloadLength = (uint)(entry.Length - DiskCatalogMakerV5Format.TableHeaderSize);
            if (payloadLength != expectedPayloadLength)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table 0x{entry.TableId:X} declares payload length {payloadLength} but its directory entry implies {expectedPayloadLength} (offset {entry.Offset}, header value {headerValue}).");
            }

            var payloadStart = entry.Offset + DiskCatalogMakerV5Format.TableHeaderSize;
            var payloadEnd = entry.Offset + entry.Length;
            var isKnown = IsKnownTable(entry.TableId);
            if (isKnown && headerValue != DiskCatalogMakerV5Format.KnownTableHeaderValue)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"table 0x{entry.TableId:X} has unsupported header value {headerValue} (offset {entry.Offset}, size {entry.Length}).");
            }

            switch (entry.TableId)
            {
                case DiskCatalogMakerV5Format.VolumeDataTableId:
                    ReadVolumeData(reader, entry, payloadStart, payloadEnd, volumes);
                    break;
                case DiskCatalogMakerV5Format.VolumeDirTableId:
                    ReadVolumeDirectory(reader, entry, payloadStart, payloadEnd, items);
                    break;
                case DiskCatalogMakerV5Format.VolumeNameTableId:
                case DiskCatalogMakerV5Format.VolumeMutableStringTableId:
                    ReadStringTable(reader, entry, payloadStart, payloadEnd, strings, warnings);
                    break;
                case DiskCatalogMakerV5Format.VolumeAliasTableId:
                    ReadAliases(reader, entry, payloadStart, payloadEnd, aliases, warnings);
                    break;
                case DiskCatalogMakerV5Format.VolumeThumbnailTableId:
                    ReadThumbnails(reader, entry, payloadStart, payloadEnd, thumbnails, warnings);
                    break;
                default:
                    unknownTables.Add(ReadUnknownTable(reader, entry, headerValue, payloadStart, payloadEnd, warnings));
                    break;
            }

            tableInfos.Add(new DiskCatalogMakerV5TableInfo(
                entry.TableId,
                headerValue,
                entry.Offset,
                entry.Length,
                entry.RecordCount,
                isKnown));
        }

        return BuildCatalog(revision, strings, volumes, items, aliases, thumbnails, unknownTables, tableInfos, warnings);
    }

    private static void ReadVolumeData(
        DiskCatalogMakerV5BinaryReader reader,
        DirectoryEntry entry,
        int payloadStart,
        int payloadEnd,
        List<VolumeRecord> volumes)
    {
        ValidateFixedPayload(entry, payloadStart, payloadEnd, DiskCatalogMakerV5Format.VolumeDataRecordSize);
        reader.Seek(payloadStart, $"table 0x{entry.TableId:X} payload");

        var identifiers = new HashSet<uint>();
        for (var index = 0; index < entry.RecordCount; index++)
        {
            var volumeId = reader.ReadUInt32($"volume record {index} identifier");
            var nameStringId = reader.ReadUInt32($"volume record {index} name string");
            var sourceStringId = reader.ReadUInt32($"volume record {index} source string");
            reader.Skip(sizeof(uint), $"volume record {index} reserved");
            var capacityBytes = reader.ReadUInt64($"volume record {index} capacity");
            var freeBytes = reader.ReadUInt64($"volume record {index} free space");
            var createdUnix = reader.ReadInt64($"volume record {index} creation date");
            var scannedUnix = reader.ReadInt64($"volume record {index} scan date");
            var kind = reader.ReadUInt16($"volume record {index} kind");
            var fileSystem = reader.ReadUInt16($"volume record {index} file system");
            reader.Skip(sizeof(ushort) * 2, $"volume record {index} flags");

            if (!identifiers.Add(volumeId))
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"volume identifier {volumeId} appears more than once in table 0x{entry.TableId:X}.");
            }

            volumes.Add(new VolumeRecord(
                volumeId,
                nameStringId,
                sourceStringId,
                capacityBytes,
                freeBytes,
                createdUnix,
                scannedUnix,
                kind,
                fileSystem));
        }
    }

    private static void ReadVolumeDirectory(
        DiskCatalogMakerV5BinaryReader reader,
        DirectoryEntry entry,
        int payloadStart,
        int payloadEnd,
        List<ItemRecord> items)
    {
        ValidateFixedPayload(entry, payloadStart, payloadEnd, DiskCatalogMakerV5Format.VolumeDirRecordSize);
        reader.Seek(payloadStart, $"table 0x{entry.TableId:X} payload");

        var identifiers = new HashSet<(uint VolumeId, uint ItemId)>();
        for (var index = 0; index < entry.RecordCount; index++)
        {
            var itemId = reader.ReadUInt32($"item record {index} identifier");
            var parentItemId = reader.ReadUInt32($"item record {index} parent identifier");
            var volumeId = reader.ReadUInt32($"item record {index} volume identifier");
            var nameStringId = reader.ReadUInt32($"item record {index} name string");
            var fullPathStringId = reader.ReadUInt32($"item record {index} full path string");
            var commentStringId = reader.ReadUInt32($"item record {index} comment string");
            var aliasTargetItemId = reader.ReadUInt32($"item record {index} alias target identifier");
            var thumbnailKey = reader.ReadUInt32($"item record {index} thumbnail key");
            var sizeBytes = reader.ReadUInt64($"item record {index} size");
            var createdUnix = reader.ReadInt64($"item record {index} creation date");
            var modifiedUnix = reader.ReadInt64($"item record {index} modification date");
            var scannedUnix = reader.ReadInt64($"item record {index} scan date");
            var typeCode = reader.ReadUInt32($"item record {index} type code");
            var creatorCode = reader.ReadUInt32($"item record {index} creator code");
            var kind = reader.ReadUInt16($"item record {index} kind");
            var flags = reader.ReadUInt16($"item record {index} flags");
            var posixMode = reader.ReadUInt16($"item record {index} POSIX mode");
            var label = reader.ReadUInt16($"item record {index} label");

            if (!identifiers.Add((volumeId, itemId)))
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"item identifier {itemId} in volume {volumeId} appears more than once in table 0x{entry.TableId:X}.");
            }

            items.Add(new ItemRecord(
                volumeId,
                itemId,
                parentItemId,
                nameStringId,
                fullPathStringId,
                commentStringId,
                aliasTargetItemId,
                thumbnailKey,
                sizeBytes,
                createdUnix,
                modifiedUnix,
                scannedUnix,
                typeCode,
                creatorCode,
                kind,
                flags,
                posixMode,
                label));
        }
    }

    private static void ReadStringTable(
        DiskCatalogMakerV5BinaryReader reader,
        DirectoryEntry entry,
        int payloadStart,
        int payloadEnd,
        Dictionary<uint, string> strings,
        WarningCollector warnings)
    {
        reader.Seek(payloadStart, $"table 0x{entry.TableId:X} payload");
        for (var index = 0; index < entry.RecordCount; index++)
        {
            EnsureTableBytes(reader.Position, payloadEnd, DiskCatalogMakerV5Format.VolumeNameRecordHeaderSize,
                entry.TableId, $"string record {index} header");

            var stringId = reader.ReadUInt32($"string record {index} identifier");
            var byteLengthRaw = reader.ReadUInt32($"string record {index} byte length");
            var encoding = reader.ReadUInt16($"string record {index} encoding");
            reader.Skip(sizeof(ushort), $"string record {index} flags");

            if (byteLengthRaw > int.MaxValue)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"string record {index} in table 0x{entry.TableId:X} declares an unsupported byte length of {byteLengthRaw}.");
            }

            var byteLength = (int)byteLengthRaw;
            EnsureTableBytes(reader.Position, payloadEnd, byteLength, entry.TableId, $"string record {index} payload");
            if (byteLength > DiskCatalogMakerV5Format.MaximumStringBytes)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"string record {index} in table 0x{entry.TableId:X} is {byteLength} byte(s), exceeding the supported maximum of {DiskCatalogMakerV5Format.MaximumStringBytes}.");
            }

            var bytes = reader.ReadSpan(byteLength, $"string record {index} payload");
            var value = DecodeString(bytes, encoding, stringId, entry.TableId);
            if (stringId == 0)
            {
                warnings.Add($"table 0x{entry.TableId:X} string record {index} uses reserved identifier 0 and was skipped.");
                continue;
            }

            if (!strings.TryAdd(stringId, value))
            {
                warnings.Add($"string identifier {stringId} appears more than once; the first value was kept.");
            }
        }

        EnsureTableConsumed(reader.Position, payloadEnd, entry.TableId, "string table");
    }

    private static void ReadAliases(
        DiskCatalogMakerV5BinaryReader reader,
        DirectoryEntry entry,
        int payloadStart,
        int payloadEnd,
        List<RawAliasRecord> aliases,
        WarningCollector warnings)
    {
        reader.Seek(payloadStart, $"table 0x{entry.TableId:X} payload");
        var parsedRecords = 0;
        for (var index = 0; index < entry.RecordCount; index++)
        {
            if ((long)reader.Position + DiskCatalogMakerV5Format.VolumeAliasRecordSize > payloadEnd)
            {
                throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"alias record {index} in table 0x{entry.TableId:X} does not fit within the table.");
            }

            var aliasId = reader.ReadUInt32($"alias record {index} identifier");
            var itemId = reader.ReadUInt32($"alias record {index} item identifier");
            var targetVolumeId = reader.ReadUInt32($"alias record {index} target volume");
            var targetDirectoryId = reader.ReadUInt32($"alias record {index} target directory");
            var mountPointStringId = reader.ReadUInt32($"alias record {index} mount point string");
            var posixPathStringId = reader.ReadUInt32($"alias record {index} POSIX path string");
            var serverStringId = reader.ReadUInt32($"alias record {index} server string");
            var userStringId = reader.ReadUInt32($"alias record {index} user string");
            var zoneStringId = reader.ReadUInt32($"alias record {index} zone string");
            var unicodeNameStringId = reader.ReadUInt32($"alias record {index} Unicode name string");
            var unicodeVolumeNameStringId = reader.ReadUInt32($"alias record {index} Unicode volume name string");
            var volumeCreatedUnix = reader.ReadInt64($"alias record {index} volume creation date");
            var fileCreatedUnix = reader.ReadInt64($"alias record {index} file creation date");
            var extraLengthRaw = reader.ReadUInt32($"alias record {index} extra length");

            if (extraLengthRaw > int.MaxValue)
            {
                warnings.Add(
                    $"alias record {aliasId} in table 0x{entry.TableId:X} declares an unsupported extra length of {extraLengthRaw} bytes; alias table parsing stopped.");
                break;
            }

            var extraLength = (int)extraLengthRaw;
            if ((long)reader.Position + extraLength > payloadEnd)
            {
                warnings.Add(
                    $"alias record {aliasId} in table 0x{entry.TableId:X} points past the end of the table; alias table parsing stopped.");
                break;
            }

            byte[]? extraBytes = null;
            if (extraLength > DiskCatalogMakerV5Format.MaximumAliasExtraBytes)
            {
                reader.Skip(extraLength, $"alias record {index} extra payload");
                warnings.Add(
                    $"alias record {aliasId} carries {extraLength} byte(s) of extra data, exceeding the supported maximum of {DiskCatalogMakerV5Format.MaximumAliasExtraBytes}; the extra data was skipped.");
            }
            else if (extraLength > 0)
            {
                extraBytes = reader.ReadBytes(extraLength, $"alias record {index} extra payload");
            }

            aliases.Add(new RawAliasRecord(
                aliasId,
                itemId,
                targetVolumeId,
                targetDirectoryId,
                mountPointStringId,
                posixPathStringId,
                serverStringId,
                userStringId,
                zoneStringId,
                unicodeNameStringId,
                unicodeVolumeNameStringId,
                volumeCreatedUnix,
                fileCreatedUnix,
                extraBytes));
            parsedRecords++;
        }

        if (parsedRecords < entry.RecordCount)
        {
            warnings.Add(
                $"alias table 0x{entry.TableId:X} declared {entry.RecordCount} record(s) but only {parsedRecords} could be read; remaining data was ignored.");
        }
        else if (reader.Position < payloadEnd)
        {
            warnings.Add(
                $"alias table 0x{entry.TableId:X} contains {payloadEnd - reader.Position} trailing byte(s); they were ignored.");
        }
    }

    private static void ReadThumbnails(
        DiskCatalogMakerV5BinaryReader reader,
        DirectoryEntry entry,
        int payloadStart,
        int payloadEnd,
        List<ThumbnailRecord> thumbnails,
        WarningCollector warnings)
    {
        ValidateFixedPayload(entry, payloadStart, payloadEnd, DiskCatalogMakerV5Format.VolumeThumbnailRecordSize);
        reader.Seek(payloadStart, $"table 0x{entry.TableId:X} payload");

        var keys = new HashSet<uint>();
        for (var index = 0; index < entry.RecordCount; index++)
        {
            var key = reader.ReadUInt32($"thumbnail record {index} key");
            var itemId = reader.ReadUInt32($"thumbnail record {index} item identifier");
            var extensionStringId = reader.ReadUInt16($"thumbnail record {index} extension string");
            reader.Skip(sizeof(ushort), $"thumbnail record {index} flags");
            reader.Skip(sizeof(uint), $"thumbnail record {index} reserved");

            if (!keys.Add(key))
            {
                warnings.Add($"thumbnail key 0x{key:X8} appears more than once; the first record was kept.");
                continue;
            }

            thumbnails.Add(new ThumbnailRecord(key, itemId, extensionStringId));
        }
    }

    private static DiskCatalogMakerV5UnknownTable ReadUnknownTable(
        DiskCatalogMakerV5BinaryReader reader,
        DirectoryEntry entry,
        uint headerValue,
        int payloadStart,
        int payloadEnd,
        WarningCollector warnings)
    {
        reader.Seek(payloadStart, $"table 0x{entry.TableId:X} payload");
        var payload = reader.ReadBytes(payloadEnd - payloadStart, $"table 0x{entry.TableId:X} payload");
        warnings.Add(
            $"unknown table 0x{entry.TableId:X} at offset {entry.Offset} ({entry.Length} bytes, {entry.RecordCount} records) was preserved as opaque bytes.");
        return new DiskCatalogMakerV5UnknownTable
        {
            TableId = entry.TableId,
            HeaderValue = headerValue,
            Offset = entry.Offset,
            Length = entry.Length,
            RecordCount = entry.RecordCount,
            Payload = payload
        };
    }

    private static DiskCatalogMakerV5Catalog BuildCatalog(
        uint revision,
        Dictionary<uint, string> strings,
        List<VolumeRecord> volumeRecords,
        List<ItemRecord> itemRecords,
        List<RawAliasRecord> aliasRecords,
        List<ThumbnailRecord> thumbnailRecords,
        List<DiskCatalogMakerV5UnknownTable> unknownTables,
        List<DiskCatalogMakerV5TableInfo> tableInfos,
        WarningCollector warnings)
    {
        var volumes = new List<DiskCatalogMakerV5Volume>(volumeRecords.Count);
        foreach (var record in volumeRecords)
        {
            volumes.Add(new DiskCatalogMakerV5Volume
            {
                VolumeId = record.VolumeId,
                Name = ResolveString(strings, record.NameStringId, $"volume {record.VolumeId} name", $"Volume {record.VolumeId}", warnings),
                Source = ResolveOptionalString(strings, record.SourceStringId, $"volume {record.VolumeId} source", warnings),
                Kind = (DiskCatalogMakerV5VolumeKind)record.Kind,
                FileSystem = (DiskCatalogMakerV5FileSystem)record.FileSystem,
                CapacityBytes = record.CapacityBytes,
                FreeBytes = record.FreeBytes,
                Created = ToTimestamp(record.CreatedUnix, $"volume {record.VolumeId} creation", warnings),
                Scanned = ToTimestamp(record.ScannedUnix, $"volume {record.VolumeId} scan", warnings)
            });
        }

        var items = new List<DiskCatalogMakerV5Item>(itemRecords.Count);
        foreach (var record in itemRecords)
        {
            var rawKind = (int)record.Kind;
            var kind = Enum.IsDefined(typeof(DiskCatalogMakerV5ItemKind), rawKind)
                ? (DiskCatalogMakerV5ItemKind)rawKind
                : DiskCatalogMakerV5ItemKind.File;
            if (!Enum.IsDefined(typeof(DiskCatalogMakerV5ItemKind), rawKind))
            {
                warnings.Add($"item {record.ItemId} in volume {record.VolumeId} uses unknown kind {record.Kind}; treated as a file.");
            }

            var hasCreated = (record.Flags & 0x0001) != 0;
            var hasModified = (record.Flags & 0x0002) != 0;
            var hasScanned = (record.Flags & 0x0004) != 0;
            var hasLabel = (record.Flags & 0x0008) != 0 || record.Label != 0;

            items.Add(new DiskCatalogMakerV5Item
            {
                VolumeId = record.VolumeId,
                ItemId = record.ItemId,
                ParentItemId = record.ParentItemId == DiskCatalogMakerV5Format.NoParentItemId ? null : record.ParentItemId,
                Name = ResolveString(strings, record.NameStringId, $"item {record.ItemId} name", $"Item {record.ItemId}", warnings),
                FullPath = ResolveOptionalString(strings, record.FullPathStringId, $"item {record.ItemId} full path", warnings),
                Comment = ResolveOptionalString(strings, record.CommentStringId, $"item {record.ItemId} comment", warnings),
                AliasTargetItemId = record.AliasTargetItemId == 0 ? null : record.AliasTargetItemId,
                ThumbnailKey = record.ThumbnailKey == 0 ? null : record.ThumbnailKey,
                SizeBytes = record.SizeBytes,
                Created = hasCreated ? ToTimestamp(record.CreatedUnix, $"item {record.ItemId} creation", warnings) : null,
                Modified = hasModified ? ToTimestamp(record.ModifiedUnix, $"item {record.ItemId} modification", warnings) : null,
                Scanned = hasScanned ? ToTimestamp(record.ScannedUnix, $"item {record.ItemId} scan", warnings) : null,
                TypeCode = FormatFourCc(record.TypeCode),
                CreatorCode = FormatFourCc(record.CreatorCode),
                Kind = kind,
                HasLabel = hasLabel,
                Label = record.Label,
                PosixMode = record.PosixMode == 0 ? null : record.PosixMode
            });
        }

        var aliases = new List<DiskCatalogMakerV5Alias>();
        var aliasItemIds = new HashSet<uint>();
        foreach (var record in aliasRecords)
        {
            if (!aliasItemIds.Add(record.ItemId))
            {
                warnings.Add($"alias for item {record.ItemId} appears more than once; the first record was used.");
                continue;
            }

            aliases.Add(new DiskCatalogMakerV5Alias
            {
                AliasId = record.AliasId,
                ItemId = record.ItemId,
                TargetVolumeId = record.TargetVolumeId,
                TargetDirectoryId = record.TargetDirectoryId,
                MountPoint = ResolveOptionalString(strings, record.MountPointStringId, $"alias {record.AliasId} mount point", warnings),
                PosixPath = ResolveOptionalString(strings, record.PosixPathStringId, $"alias {record.AliasId} POSIX path", warnings),
                Server = ResolveOptionalString(strings, record.ServerStringId, $"alias {record.AliasId} server", warnings),
                User = ResolveOptionalString(strings, record.UserStringId, $"alias {record.AliasId} user", warnings),
                Zone = ResolveOptionalString(strings, record.ZoneStringId, $"alias {record.AliasId} zone", warnings),
                UnicodeName = ResolveOptionalString(strings, record.UnicodeNameStringId, $"alias {record.AliasId} Unicode name", warnings),
                UnicodeVolumeName = ResolveOptionalString(strings, record.UnicodeVolumeNameStringId, $"alias {record.AliasId} Unicode volume name", warnings),
                VolumeCreated = ToTimestamp(record.VolumeCreatedUnix, $"alias {record.AliasId} volume creation", warnings),
                FileCreated = ToTimestamp(record.FileCreatedUnix, $"alias {record.AliasId} file creation", warnings),
                RecursiveExtraBytes = record.ExtraBytes
            });
        }

        var thumbnails = new List<DiskCatalogMakerV5Thumbnail>();
        foreach (var record in thumbnailRecords)
        {
            var extension = ResolveOptionalString(strings, record.ExtensionStringId, $"thumbnail 0x{record.Key:X8} extension", warnings);
            thumbnails.Add(new DiskCatalogMakerV5Thumbnail
            {
                Key = record.Key,
                ItemId = record.ItemId,
                Extension = DiskCatalogMakerV5ThumbnailResolver.NormalizeExtension(extension)
            });
        }

        return new DiskCatalogMakerV5Catalog
        {
            Revision = revision,
            Volumes = volumes,
            Items = items,
            Aliases = aliases,
            Thumbnails = thumbnails,
            UnknownTables = unknownTables,
            Tables = tableInfos,
            Warnings = warnings.ToList()
        };
    }

    private static string ResolveString(
        Dictionary<uint, string> strings,
        uint stringId,
        string context,
        string fallback,
        WarningCollector warnings)
    {
        if (stringId == 0)
        {
            warnings.Add($"{context} is missing; fell back to '{fallback}'.");
            return fallback;
        }

        if (strings.TryGetValue(stringId, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        warnings.Add($"{context} references unknown string identifier {stringId}; fell back to '{fallback}'.");
        return fallback;
    }

    private static string? ResolveOptionalString(
        Dictionary<uint, string> strings,
        uint stringId,
        string context,
        WarningCollector warnings)
    {
        if (stringId == 0)
        {
            return null;
        }

        if (strings.TryGetValue(stringId, out var value))
        {
            return value;
        }

        warnings.Add($"{context} references unknown string identifier {stringId}; the value was omitted.");
        return null;
    }

    private static string DecodeString(ReadOnlySpan<byte> bytes, ushort encoding, uint stringId, uint tableId)
    {
        try
        {
            var decoded = encoding switch
            {
                DiskCatalogMakerV5Format.Utf8Encoding => StrictUtf8.GetString(bytes),
                DiskCatalogMakerV5Format.Utf16LittleEndianEncoding => DecodeUtf16(bytes, stringId, tableId),
                _ => throw new DiskCatalogMakerV5CorruptCatalogException(
                    $"string {stringId} in table 0x{tableId:X} uses unsupported encoding tag {encoding}.")
            };

            return decoded.TrimEnd('\0');
        }
        catch (DecoderFallbackException exception)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"string {stringId} in table 0x{tableId:X} is not valid {DescribeEncoding(encoding)}: {exception.Message}");
        }
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> bytes, uint stringId, uint tableId)
    {
        if ((bytes.Length & 1) != 0)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"string {stringId} in table 0x{tableId:X} has an odd byte length {bytes.Length} for UTF-16.");
        }

        return StrictUtf16LittleEndian.GetString(bytes);
    }

    private static string DescribeEncoding(ushort encoding) => encoding switch
    {
        DiskCatalogMakerV5Format.Utf8Encoding => "UTF-8",
        DiskCatalogMakerV5Format.Utf16LittleEndianEncoding => "UTF-16LE",
        _ => $"encoding {encoding}"
    };

    private static DateTimeOffset? ToTimestamp(long unixSeconds, string context, WarningCollector warnings)
    {
        if (unixSeconds <= 0)
        {
            return null;
        }

        if (unixSeconds > 253402300799)
        {
            warnings.Add($"{context} date {unixSeconds} is outside the supported range and was ignored.");
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    }

    private static string? FormatFourCc(uint value)
    {
        if (value == 0)
        {
            return null;
        }

        var builder = new StringBuilder(4);
        for (var shift = 24; shift >= 0; shift -= 8)
        {
            var character = (byte)(value >> shift);
            if (character is >= 0x20 and <= 0x7E)
            {
                builder.Append((char)character);
            }
            else
            {
                builder.Append("\\x").Append(character.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static void ValidateFixedPayload(DirectoryEntry entry, int payloadStart, int payloadEnd, int recordSize)
    {
        var expected = (long)entry.RecordCount * recordSize;
        var actual = payloadEnd - payloadStart;
        if (expected != actual)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"table 0x{entry.TableId:X} declares {entry.RecordCount} record(s) of {recordSize} byte(s) ({expected} bytes) but its payload is {actual} byte(s).");
        }
    }

    private static void EnsureTableBytes(int position, int payloadEnd, int requested, uint tableId, string context)
    {
        if (requested < 0 || (long)position + requested > payloadEnd)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"{context} at offset {position} requests {requested} byte(s) but table 0x{tableId:X} ends at offset {payloadEnd}.");
        }
    }

    private static void EnsureTableConsumed(int position, int payloadEnd, uint tableId, string tableName)
    {
        if (position != payloadEnd)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"{tableName} 0x{tableId:X} contains {payloadEnd - position} trailing byte(s) after its declared records.");
        }
    }

    private static bool IsKnownTable(uint tableId) => tableId is
        DiskCatalogMakerV5Format.VolumeDataTableId or
        DiskCatalogMakerV5Format.VolumeDirTableId or
        DiskCatalogMakerV5Format.VolumeNameTableId or
        DiskCatalogMakerV5Format.VolumeAliasTableId or
        DiskCatalogMakerV5Format.VolumeThumbnailTableId or
        DiskCatalogMakerV5Format.VolumeMutableStringTableId;

    private static int ReadNonNegativeInt(uint value, string context, string source)
    {
        if (value > int.MaxValue)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException($"{context} {value} exceeds the supported range ({source}).");
        }

        return (int)value;
    }

    private readonly record struct Header(uint Revision, uint DirectoryOffset, uint TableCount);

    private readonly record struct DirectoryEntry(uint TableId, int Offset, int Length, uint RecordCount, uint Flags);

    private readonly record struct VolumeRecord(
        uint VolumeId,
        uint NameStringId,
        uint SourceStringId,
        ulong CapacityBytes,
        ulong FreeBytes,
        long CreatedUnix,
        long ScannedUnix,
        ushort Kind,
        ushort FileSystem);

    private readonly record struct ItemRecord(
        uint VolumeId,
        uint ItemId,
        uint ParentItemId,
        uint NameStringId,
        uint FullPathStringId,
        uint CommentStringId,
        uint AliasTargetItemId,
        uint ThumbnailKey,
        ulong SizeBytes,
        long CreatedUnix,
        long ModifiedUnix,
        long ScannedUnix,
        uint TypeCode,
        uint CreatorCode,
        ushort Kind,
        ushort Flags,
        ushort PosixMode,
        ushort Label);

    private readonly record struct RawAliasRecord(
        uint AliasId,
        uint ItemId,
        uint TargetVolumeId,
        uint TargetDirectoryId,
        uint MountPointStringId,
        uint PosixPathStringId,
        uint ServerStringId,
        uint UserStringId,
        uint ZoneStringId,
        uint UnicodeNameStringId,
        uint UnicodeVolumeNameStringId,
        long VolumeCreatedUnix,
        long FileCreatedUnix,
        byte[]? ExtraBytes);

    private readonly record struct ThumbnailRecord(uint Key, uint ItemId, uint ExtensionStringId);

    private sealed class WarningCollector
    {
        private const int MaximumWarnings = 1000;
        private readonly List<string> warnings = [];
        private bool suppressed;

        public void Add(string warning)
        {
            if (warnings.Count < MaximumWarnings)
            {
                warnings.Add(warning);
            }
            else if (!suppressed)
            {
                warnings.Add("Additional warnings were suppressed.");
                suppressed = true;
            }
        }

        public IReadOnlyList<string> ToList() => warnings;
    }
}
