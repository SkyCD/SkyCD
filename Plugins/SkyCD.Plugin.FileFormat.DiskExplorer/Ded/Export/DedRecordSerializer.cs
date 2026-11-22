using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Export;

/// <summary>
/// Serializes and parses one catalog object record. The record layout is
/// documented in <c>docs/plugins/disk-explorer-ded.md</c>; unknown kinds are
/// never parsed and are re-emitted verbatim.
/// </summary>
public static class DedRecordSerializer
{
    /// <summary>Serializes a known-kind object, including a recomputed record length.</summary>
    public static byte[] Serialize(DedCatalogObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IsUnknownKind)
        {
            throw new DedModelException("unknown object kinds are written from their preserved raw record.");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(value.RawKind);
        writer.Write(value.Flags);
        writer.Write(0u);
        writer.Write(value.ObjectId);
        writer.Write(value.ParentId);
        WriteText(writer, value.Name, "name");
        WriteText(writer, value.Path, "path");
        WriteText(writer, value.Description, "description");
        writer.Write(value.LogicalSize);
        writer.Write(value.AggregateSize);
        writer.Write(value.CreatedFileTime);
        writer.Write(value.ModifiedFileTime);
        writer.Write(value.AddedFileTime);
        writer.Write(value.FileCount);
        writer.Write(value.FolderCount);
        WriteIds(writer, value.ChildIds);
        WriteIds(writer, value.CategoryIds);
        WriteExtensions(writer, value.Extensions);
        WritePayloads(writer, value.Payloads);
        var tail = value.Tail ?? [];
        writer.Write((uint)tail.Length);
        writer.Write(tail);
        writer.Flush();

        var record = stream.ToArray();
        if ((ulong)record.Length > uint.MaxValue)
        {
            throw new DedModelException("object record exceeds the 32-bit record length field.");
        }

        var length = (uint)record.Length;
        record[8] = (byte)length;
        record[9] = (byte)(length >> 8);
        record[10] = (byte)(length >> 16);
        record[11] = (byte)(length >> 24);
        return record;
    }

    /// <summary>Parses one known-kind record that already fits <paramref name="options"/> limits.</summary>
    public static DedCatalogObject Parse(byte[] record, DedReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(options);

        var cursor = new DedByteCursor(record, options);
        var rawKind = cursor.ReadUInt32("object kind");
        if (!DedObjectKinds.IsKnownValue(rawKind))
        {
            throw new DedCorruptException("unknown object kinds must be preserved as raw records.");
        }

        var result = new DedCatalogObject
        {
            RawKind = rawKind,
            Flags = cursor.ReadUInt32("object flags")
        };

        var recordLength = cursor.ReadUInt32("record length");
        if (recordLength != record.Length)
        {
            throw new DedCorruptException("record length does not match the stored record extent.");
        }

        result.ObjectId = cursor.ReadUInt64("object id");
        result.ParentId = cursor.ReadUInt64("parent id");
        result.Name = ReadText(cursor, "name");
        result.Path = ReadText(cursor, "path");
        result.Description = ReadText(cursor, "description");
        result.LogicalSize = cursor.ReadUInt64("logical size");
        result.AggregateSize = cursor.ReadUInt64("aggregate size");
        result.CreatedFileTime = cursor.ReadUInt64("created timestamp");
        result.ModifiedFileTime = cursor.ReadUInt64("modified timestamp");
        result.AddedFileTime = cursor.ReadUInt64("added timestamp");
        result.FileCount = cursor.ReadUInt32("file count");
        result.FolderCount = cursor.ReadUInt32("folder count");
        ReadIds(cursor, result.ChildIds, options, "child");
        ReadIds(cursor, result.CategoryIds, options, "category");
        ReadExtensions(cursor, result.Extensions, options);
        ReadPayloads(cursor, result.Payloads, options);
        var tailLength = cursor.ReadLength("tail", options.MaxRecordBytes);
        if (tailLength > 0)
        {
            result.Tail = cursor.ReadBytes(tailLength, "tail");
        }

        cursor.RequireComplete();
        return result;
    }

    /// <summary>Serializes a list of extension fields for row metadata.</summary>
    public static byte[] SerializeExtensions(IEnumerable<DedExtensionField> extensions)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteExtensions(writer, extensions);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Parses extension fields from row metadata.</summary>
    public static List<DedExtensionField> ParseExtensions(byte[]? blob, DedReadOptions? options = null)
    {
        var result = new List<DedExtensionField>();
        if (blob is null || blob.Length == 0)
        {
            return result;
        }

        var cursor = new DedByteCursor(blob, options ?? new DedReadOptions());
        ReadExtensions(cursor, result, options ?? new DedReadOptions());
        cursor.RequireComplete();
        return result;
    }

    /// <summary>Serializes a list of opaque payloads for row metadata.</summary>
    public static byte[] SerializePayloads(IEnumerable<DedPayload> payloads)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WritePayloads(writer, payloads);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Parses opaque payloads from row metadata.</summary>
    public static List<DedPayload> ParsePayloads(byte[]? blob, DedReadOptions? options = null)
    {
        var result = new List<DedPayload>();
        if (blob is null || blob.Length == 0)
        {
            return result;
        }

        var cursor = new DedByteCursor(blob, options ?? new DedReadOptions());
        ReadPayloads(cursor, result, options ?? new DedReadOptions());
        cursor.RequireComplete();
        return result;
    }

    private static void WriteText(BinaryWriter writer, DedText text, string field)
    {
        var value = text ?? DedText.Empty;
        var bytes = DedTextCodec.Encode(value, field);
        writer.Write((uint)value.Encoding);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }

    private static DedText ReadText(DedByteCursor cursor, string field)
    {
        var encoding = cursor.ReadUInt32(field + " encoding");
        var length = cursor.ReadStringLength(field);
        var bytes = length == 0 ? [] : cursor.ReadBytes(length, field);
        return DedTextCodec.Decode(bytes, encoding, field);
    }

    private static void WriteIds(BinaryWriter writer, IReadOnlyList<ulong> ids)
    {
        writer.Write((uint)ids.Count);
        foreach (var id in ids)
        {
            writer.Write(id);
        }
    }

    private static void ReadIds(DedByteCursor cursor, List<ulong> target, DedReadOptions options, string field)
    {
        var count = cursor.ReadCount(field + " ids", 8, options.MaxObjects);
        for (var i = 0; i < count; i++)
        {
            target.Add(cursor.ReadUInt64(field + " id"));
        }
    }

    private static void WriteExtensions(BinaryWriter writer, IEnumerable<DedExtensionField> extensions)
    {
        var list = extensions as IReadOnlyCollection<DedExtensionField> ?? new List<DedExtensionField>(extensions);
        writer.Write((uint)list.Count);
        foreach (var extension in list)
        {
            writer.Write(extension.Tag);
            writer.Write((uint)extension.Data.Length);
            writer.Write(extension.Data);
        }
    }

    private static void ReadExtensions(DedByteCursor cursor, List<DedExtensionField> target, DedReadOptions options)
    {
        var count = cursor.ReadCount("extension count", 8, options.MaxObjects);
        for (var i = 0; i < count; i++)
        {
            var tag = cursor.ReadUInt32("extension tag");
            var length = cursor.ReadPayloadLength("extension length");
            target.Add(new DedExtensionField(tag, cursor.ReadBytes(length, "extension data")));
        }
    }

    private static void WritePayloads(BinaryWriter writer, IEnumerable<DedPayload> payloads)
    {
        var list = payloads as IReadOnlyCollection<DedPayload> ?? new List<DedPayload>(payloads);
        writer.Write((uint)list.Count);
        foreach (var payload in list)
        {
            writer.Write(payload.RawKind);
            writer.Write((uint)payload.Data.Length);
            writer.Write(payload.Data);
        }
    }

    private static void ReadPayloads(DedByteCursor cursor, List<DedPayload> target, DedReadOptions options)
    {
        var count = cursor.ReadCount("payload count", 8, options.MaxObjects);
        for (var i = 0; i < count; i++)
        {
            var kind = cursor.ReadUInt32("payload kind");
            var length = cursor.ReadPayloadLength("payload length");
            target.Add(new DedPayload(kind, cursor.ReadBytes(length, "payload data")));
        }
    }
}
