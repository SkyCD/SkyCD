using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

/// <summary>
/// Builds synthetic v5 catalog payloads that follow the layout documented in
/// docs/plugins/diskcatalogmaker-v5.md. Fixtures are authored for this repository.
/// </summary>
internal sealed class DiskCatalogMakerV5FixtureBuilder
{
    private readonly List<StoredString> strings = [];
    private readonly List<VolumeSpec> volumes = [];
    private readonly List<ItemSpec> items = [];
    private readonly List<AliasSpec> aliases = [];
    private readonly List<ThumbnailSpec> thumbnails = [];
    private readonly List<UnknownTableSpec> unknownTables = [];

    public ushort MajorVersion { get; set; } = DiskCatalogMakerV5Format.MajorVersion;

    public ushort MinorVersion { get; set; }

    public ushort HeaderSize { get; set; } = DiskCatalogMakerV5Format.HeaderSize;

    public uint Revision { get; set; } = 1;

    public uint AddString(string value, ushort encoding = DiskCatalogMakerV5Format.Utf8Encoding)
    {
        var id = (uint)(strings.Count + 1);
        strings.Add(new StoredString(id, value, encoding));
        return id;
    }

    public uint AddVolume(
        uint volumeId,
        string name,
        DiskCatalogMakerV5VolumeKind kind = DiskCatalogMakerV5VolumeKind.Disk,
        DiskCatalogMakerV5FileSystem fileSystem = DiskCatalogMakerV5FileSystem.HfsPlus,
        ulong capacityBytes = 500_000_000_000,
        ulong freeBytes = 100_000_000_000,
        long? createdUnix = null,
        long? scannedUnix = null,
        string? source = null,
        ushort nameEncoding = DiskCatalogMakerV5Format.Utf8Encoding)
    {
        volumes.Add(new VolumeSpec(
            volumeId,
            AddString(name, nameEncoding),
            source is null ? 0 : AddString(source),
            capacityBytes,
            freeBytes,
            createdUnix ?? 0,
            scannedUnix ?? 0,
            kind,
            fileSystem));
        return volumeId;
    }

    public uint AddItem(
        uint volumeId,
        uint itemId,
        uint? parentItemId,
        string name,
        DiskCatalogMakerV5ItemKind kind = DiskCatalogMakerV5ItemKind.File,
        ulong sizeBytes = 0,
        long? createdUnix = null,
        long? modifiedUnix = null,
        long? scannedUnix = null,
        string? typeCode = null,
        string? creatorCode = null,
        string? comment = null,
        int? label = null,
        int? posixMode = null,
        string? fullPath = null,
        uint? aliasTargetItemId = null,
        uint? thumbnailKey = null,
        ushort nameEncoding = DiskCatalogMakerV5Format.Utf8Encoding)
    {
        items.Add(new ItemSpec(
            volumeId,
            itemId,
            parentItemId,
            AddString(name, nameEncoding),
            fullPath is null ? 0 : AddString(fullPath),
            comment is null ? 0 : AddString(comment),
            aliasTargetItemId ?? 0,
            thumbnailKey ?? 0,
            sizeBytes,
            createdUnix ?? 0,
            modifiedUnix ?? 0,
            scannedUnix ?? 0,
            ToFourCc(typeCode),
            ToFourCc(creatorCode),
            kind,
            BuildItemFlags(createdUnix, modifiedUnix, scannedUnix, label),
            posixMode ?? 0,
            label ?? 0));
        return itemId;
    }

    public void AddAlias(
        uint aliasId,
        uint itemId,
        uint targetVolumeId = 0,
        uint targetDirectoryId = 0,
        string? mountPoint = null,
        string? posixPath = null,
        string? server = null,
        string? user = null,
        string? zone = null,
        string? unicodeName = null,
        string? unicodeVolumeName = null,
        long? volumeCreatedUnix = null,
        long? fileCreatedUnix = null,
        byte[]? extraBytes = null)
    {
        aliases.Add(new AliasSpec(
            aliasId,
            itemId,
            targetVolumeId,
            targetDirectoryId,
            mountPoint is null ? 0 : AddString(mountPoint),
            posixPath is null ? 0 : AddString(posixPath),
            server is null ? 0 : AddString(server),
            user is null ? 0 : AddString(user),
            zone is null ? 0 : AddString(zone),
            unicodeName is null ? 0 : AddString(unicodeName),
            unicodeVolumeName is null ? 0 : AddString(unicodeVolumeName),
            volumeCreatedUnix ?? 0,
            fileCreatedUnix ?? 0,
            extraBytes ?? []));
    }

    public void AddThumbnail(uint key, uint itemId, string? extension)
    {
        thumbnails.Add(new ThumbnailSpec(key, itemId, extension is null ? 0 : AddString(extension)));
    }

    public void AddUnknownTable(uint tableId, uint headerValue, byte[] payload, uint recordCount = 0)
    {
        unknownTables.Add(new UnknownTableSpec(tableId, headerValue, payload, recordCount));
    }

    public byte[] Build()
    {
        var tables = new List<TableSpec>();
        var stringPayload = BuildStringPayload();
        if (stringPayload.Length > 0)
        {
            tables.Add(new TableSpec(
                DiskCatalogMakerV5Format.VolumeNameTableId,
                DiskCatalogMakerV5Format.KnownTableHeaderValue,
                (uint)strings.Count,
                stringPayload));
        }

        if (volumes.Count > 0)
        {
            tables.Add(new TableSpec(
                DiskCatalogMakerV5Format.VolumeDataTableId,
                DiskCatalogMakerV5Format.KnownTableHeaderValue,
                (uint)volumes.Count,
                BuildVolumePayload()));
        }

        if (items.Count > 0)
        {
            tables.Add(new TableSpec(
                DiskCatalogMakerV5Format.VolumeDirTableId,
                DiskCatalogMakerV5Format.KnownTableHeaderValue,
                (uint)items.Count,
                BuildItemPayload()));
        }

        if (aliases.Count > 0)
        {
            tables.Add(new TableSpec(
                DiskCatalogMakerV5Format.VolumeAliasTableId,
                DiskCatalogMakerV5Format.KnownTableHeaderValue,
                (uint)aliases.Count,
                BuildAliasPayload()));
        }

        if (thumbnails.Count > 0)
        {
            tables.Add(new TableSpec(
                DiskCatalogMakerV5Format.VolumeThumbnailTableId,
                DiskCatalogMakerV5Format.KnownTableHeaderValue,
                (uint)thumbnails.Count,
                BuildThumbnailPayload()));
        }

        foreach (var unknown in unknownTables)
        {
            tables.Add(new TableSpec(unknown.TableId, unknown.HeaderValue, unknown.RecordCount, unknown.Payload));
        }

        var directoryOffset = (int)HeaderSize;
        var directoryLength = tables.Count * DiskCatalogMakerV5Format.DirectoryEntrySize;
        var tableOffset = directoryOffset + directoryLength;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Header
        writer.Write((byte)'D');
        writer.Write((byte)'C');
        writer.Write((byte)'M');
        writer.Write((byte)'5');
        writer.Write(HeaderSize);
        writer.Write(MajorVersion);
        writer.Write(MinorVersion);
        writer.Write(DiskCatalogMakerV5Format.LittleEndianMarker);
        writer.Write(Revision);
        writer.Write((uint)directoryOffset);
        writer.Write((uint)tables.Count);
        writer.Write(0u);
        writer.Write(0u);

        // Directory
        var offsets = new List<int>(tables.Count);
        foreach (var table in tables)
        {
            offsets.Add(tableOffset);
            writer.Write(table.TableId);
            writer.Write((uint)tableOffset);
            writer.Write((uint)(DiskCatalogMakerV5Format.TableHeaderSize + table.Payload.Length));
            writer.Write(table.RecordCount);
            writer.Write(0u);
            tableOffset += DiskCatalogMakerV5Format.TableHeaderSize + table.Payload.Length;
        }

        // Tables
        for (var index = 0; index < tables.Count; index++)
        {
            var table = tables[index];
            writer.Write(table.TableId);
            writer.Write(table.HeaderValue);
            writer.Write(table.RecordCount);
            writer.Write((uint)table.Payload.Length);
            writer.Write(table.Payload);
        }

        return stream.ToArray();
    }

    public static void PatchUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    public static void PatchUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    public static int FindTableOffset(byte[] data, uint tableId)
    {
        var directoryOffset = (int)ReadUInt32(data, 16);
        var tableCount = (int)ReadUInt32(data, 20);
        for (var index = 0; index < tableCount; index++)
        {
            var entryOffset = directoryOffset + index * DiskCatalogMakerV5Format.DirectoryEntrySize;
            if (ReadUInt32(data, entryOffset) == tableId)
            {
                return (int)ReadUInt32(data, entryOffset + 4);
            }
        }

        throw new InvalidOperationException($"Table 0x{tableId:X} not found.");
    }

    public static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private byte[] BuildStringPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var stored in strings)
        {
            var bytes = stored.Encoding == DiskCatalogMakerV5Format.Utf16LittleEndianEncoding
                ? Encoding.Unicode.GetBytes(stored.Value)
                : Encoding.UTF8.GetBytes(stored.Value);
            writer.Write(stored.Id);
            writer.Write((uint)bytes.Length);
            writer.Write(stored.Encoding);
            writer.Write((ushort)0);
            writer.Write(bytes);
        }

        return stream.ToArray();
    }

    private byte[] BuildVolumePayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var volume in volumes)
        {
            writer.Write(volume.VolumeId);
            writer.Write(volume.NameStringId);
            writer.Write(volume.SourceStringId);
            writer.Write(0u);
            writer.Write(volume.CapacityBytes);
            writer.Write(volume.FreeBytes);
            writer.Write(volume.CreatedUnix);
            writer.Write(volume.ScannedUnix);
            writer.Write((ushort)volume.Kind);
            writer.Write((ushort)volume.FileSystem);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
        }

        return stream.ToArray();
    }

    private byte[] BuildItemPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var item in items)
        {
            writer.Write(item.ItemId);
            writer.Write(item.ParentItemId ?? DiskCatalogMakerV5Format.NoParentItemId);
            writer.Write(item.VolumeId);
            writer.Write(item.NameStringId);
            writer.Write(item.FullPathStringId);
            writer.Write(item.CommentStringId);
            writer.Write(item.AliasTargetItemId);
            writer.Write(item.ThumbnailKey);
            writer.Write(item.SizeBytes);
            writer.Write(item.CreatedUnix);
            writer.Write(item.ModifiedUnix);
            writer.Write(item.ScannedUnix);
            writer.Write(item.TypeCode);
            writer.Write(item.CreatorCode);
            writer.Write((ushort)item.Kind);
            writer.Write(item.Flags);
            writer.Write((ushort)item.PosixMode);
            writer.Write((ushort)item.Label);
        }

        return stream.ToArray();
    }

    private byte[] BuildAliasPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var alias in aliases)
        {
            writer.Write(alias.AliasId);
            writer.Write(alias.ItemId);
            writer.Write(alias.TargetVolumeId);
            writer.Write(alias.TargetDirectoryId);
            writer.Write(alias.MountPointStringId);
            writer.Write(alias.PosixPathStringId);
            writer.Write(alias.ServerStringId);
            writer.Write(alias.UserStringId);
            writer.Write(alias.ZoneStringId);
            writer.Write(alias.UnicodeNameStringId);
            writer.Write(alias.UnicodeVolumeNameStringId);
            writer.Write(alias.VolumeCreatedUnix);
            writer.Write(alias.FileCreatedUnix);
            writer.Write((uint)alias.ExtraBytes.Length);
            writer.Write(alias.ExtraBytes);
        }

        return stream.ToArray();
    }

    private byte[] BuildThumbnailPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var thumbnail in thumbnails)
        {
            writer.Write(thumbnail.Key);
            writer.Write(thumbnail.ItemId);
            writer.Write((ushort)thumbnail.ExtensionStringId);
            writer.Write((ushort)0);
            writer.Write(0u);
        }

        return stream.ToArray();
    }

    private static ushort BuildItemFlags(long? createdUnix, long? modifiedUnix, long? scannedUnix, int? label)
    {
        var flags = 0;
        if (createdUnix is > 0) flags |= 0x0001;
        if (modifiedUnix is > 0) flags |= 0x0002;
        if (scannedUnix is > 0) flags |= 0x0004;
        if (label is not null) flags |= 0x0008;
        return (ushort)flags;
    }

    private static uint ToFourCc(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var chars = value.Length >= 4 ? value[..4] : value.PadRight(4, ' ');
        return ((uint)chars[0] << 24) | ((uint)chars[1] << 16) | ((uint)chars[2] << 8) | chars[3];
    }

    private readonly record struct StoredString(uint Id, string Value, ushort Encoding);

    private readonly record struct VolumeSpec(
        uint VolumeId,
        uint NameStringId,
        uint SourceStringId,
        ulong CapacityBytes,
        ulong FreeBytes,
        long CreatedUnix,
        long ScannedUnix,
        DiskCatalogMakerV5VolumeKind Kind,
        DiskCatalogMakerV5FileSystem FileSystem);

    private readonly record struct ItemSpec(
        uint VolumeId,
        uint ItemId,
        uint? ParentItemId,
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
        DiskCatalogMakerV5ItemKind Kind,
        ushort Flags,
        int PosixMode,
        int Label);

    private readonly record struct AliasSpec(
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
        byte[] ExtraBytes);

    private readonly record struct ThumbnailSpec(uint Key, uint ItemId, uint ExtensionStringId);

    private readonly record struct UnknownTableSpec(uint TableId, uint HeaderValue, byte[] Payload, uint RecordCount);

    private readonly record struct TableSpec(uint TableId, uint HeaderValue, uint RecordCount, byte[] Payload);
}
