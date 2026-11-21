using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Plugin.DiskExplorer.Ded.Exceptions;
using SkyCD.Plugin.DiskExplorer.Ded.Export;
using SkyCD.Plugin.DiskExplorer.Ded.Models;

namespace SkyCD.Plugin.DiskExplorer.Ded.Parsing;

/// <summary>
/// Reads a DED container into <see cref="DedDatabase"/>. Every count, offset, and
/// length is validated against configured bounds before use; records may be
/// shared through duplicate table offsets but must never partially overlap.
/// </summary>
public static class DedReader
{
    /// <summary>Reads a database from the beginning of a stream.</summary>
    public static DedDatabase Read(Stream source, DedReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new DedReadOptions();

        if (source.CanSeek)
        {
            return ReadSeekable(source, options, cancellationToken);
        }

        using var buffered = Buffer(source, options, cancellationToken);
        return ReadSeekable(buffered, options, cancellationToken);
    }

    private static DedDatabase ReadSeekable(Stream stream, DedReadOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = stream.Length;
        if (length < DedFormat.HeaderSize)
        {
            throw new DedCorruptException("file is shorter than the fixed header.");
        }

        var header = ReadAt(stream, 0, DedFormat.HeaderSize);
        for (var i = 0; i < DedFormat.Magic.Length; i++)
        {
            if (header[i] != DedFormat.Magic[i])
            {
                throw new DedCorruptException("fixed header magic is not DEDB.");
            }
        }

        var version = ReadUInt32(header, 0x04);
        if (version != DedFormat.CurrentVersion)
        {
            throw new DedUnsupportedVersionException(version);
        }

        var tableCount = ReadUInt32(header, 0x08);
        var populatedCount = ReadUInt32(header, 0x0C);
        var headerFlags = ReadUInt32(header, DedFormat.FlagsOffset);
        var creationTime = ReadUInt64(header, DedFormat.CreationTimeOffset);
        var reservedFlags = ReadUInt32(header, DedFormat.ReservedFlagsOffset);

        if (tableCount > (uint)options.MaxTableEntries)
        {
            throw new DedLimitExceededException("offset-table slots", tableCount);
        }

        var tableBytes = (ulong)tableCount * 4UL;
        var tableEnd = (ulong)DedFormat.HeaderSize + tableBytes;
        if (tableEnd > (ulong)length)
        {
            throw new DedCorruptException("offset table extends beyond the end of the file.");
        }

        if (populatedCount > tableCount)
        {
            throw new DedCorruptException("populated record count exceeds the offset-table slot count.");
        }

        if ((headerFlags & DedFormat.HeaderFlagPasswordProtected) != 0)
        {
            if (options.Password is { Length: > 0 })
            {
                throw new DedPasswordInvalidException();
            }

            throw new DedPasswordRequiredException();
        }

        var titleBytes = new byte[DedFormat.TitleSize];
        Array.Copy(header, DedFormat.TitleOffset, titleBytes, 0, DedFormat.TitleSize);
        var database = new DedDatabase
        {
            FormatVersion = version,
            HeaderFlags = headerFlags,
            ReservedFlags = reservedFlags,
            CreationFileTime = creationTime,
            Title = DedTextCodec.DecodeHeaderTitle(titleBytes),
            TableSlotCount = (int)tableCount
        };

        var table = tableCount == 0 ? [] : ReadAt(stream, DedFormat.HeaderSize, checked((int)tableBytes));
        var offsets = new List<uint>();
        var seenOffsets = new HashSet<uint>();
        for (var slot = 0; slot < tableCount; slot++)
        {
            var offset = ReadUInt32(table, slot * 4);
            if (offset == 0)
            {
                continue;
            }

            if (seenOffsets.Add(offset))
            {
                offsets.Add(offset);
            }
        }

        if (offsets.Count > options.MaxObjects)
        {
            throw new DedLimitExceededException("object count", (ulong)offsets.Count);
        }

        if ((uint)offsets.Count != populatedCount)
        {
            throw new DedCorruptException("populated record count does not match the non-zero table entries.");
        }

        var extents = new List<(uint Offset, int Length)>(offsets.Count);
        foreach (var offset in offsets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extents.Add(ReadExtent(stream, offset, length, options));
        }

        ValidateNoPartialOverlap(extents);

        foreach (var (offset, recordLength) in extents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadAt(stream, offset, recordLength);
            database.Objects.Add(ParseRecord(record, options));
        }

        ValidateGraph(database);
        return database;
    }

    private static (uint Offset, int Length) ReadExtent(Stream stream, uint offset, long fileLength, DedReadOptions options)
    {
        if ((ulong)offset + DedFormat.ObjectPrefixSize > (ulong)fileLength)
        {
            throw new DedCorruptException("object offset points outside the file.");
        }

        var prefix = ReadAt(stream, offset, DedFormat.ObjectPrefixSize);
        var recordLength = ReadUInt32(prefix, 8);
        if (recordLength < DedFormat.ObjectPrefixSize)
        {
            throw new DedCorruptException("object record length is smaller than the record prefix.");
        }

        if (recordLength > (uint)options.MaxRecordBytes)
        {
            throw new DedLimitExceededException("object record size", recordLength);
        }

        if ((ulong)offset + recordLength > (ulong)fileLength)
        {
            throw new DedCorruptException("object record extends beyond the end of the file.");
        }

        return (offset, (int)recordLength);
    }

    private static DedCatalogObject ParseRecord(byte[] record, DedReadOptions options)
    {
        var rawKind = ReadUInt32(record, 0);
        if (DedObjectKinds.IsKnownValue(rawKind))
        {
            return DedRecordSerializer.Parse(record, options);
        }

        if (record.Length < 20)
        {
            throw new DedCorruptException("unknown object record is too short to carry identity.");
        }

        return new DedCatalogObject
        {
            RawKind = rawKind,
            Flags = ReadUInt32(record, 4),
            ObjectId = ReadUInt64(record, 12),
            ParentId = ReadUInt64(record, 20),
            RawRecord = record
        };
    }

    private static void ValidateNoPartialOverlap(List<(uint Offset, int Length)> extents)
    {
        var sorted = new List<(uint Offset, int Length)>(extents);
        sorted.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        for (var i = 1; i < sorted.Count; i++)
        {
            var previous = sorted[i - 1];
            var current = sorted[i];
            if ((ulong)current.Offset < (ulong)previous.Offset + (ulong)previous.Length)
            {
                throw new DedCorruptException("object records partially overlap.");
            }
        }
    }

    private static void ValidateGraph(DedDatabase database)
    {
        var byId = new Dictionary<ulong, DedCatalogObject>(database.Objects.Count);
        foreach (var item in database.Objects)
        {
            if (item.ObjectId == 0)
            {
                throw new DedCorruptException("object id zero is reserved.");
            }

            if (!byId.TryAdd(item.ObjectId, item))
            {
                throw new DedCorruptException("object ids are not unique.");
            }
        }

        foreach (var item in database.Objects)
        {
            if (item.ParentId != 0 && !byId.ContainsKey(item.ParentId))
            {
                throw new DedCorruptException("object references a missing parent.");
            }

            foreach (var childId in item.ChildIds)
            {
                if (!byId.ContainsKey(childId))
                {
                    throw new DedCorruptException("object references a missing child.");
                }
            }
        }

        DetectCycles(database, byId);
    }

    private static void DetectCycles(DedDatabase database, Dictionary<ulong, DedCatalogObject> byId)
    {
        var indexById = new Dictionary<ulong, int>(database.Objects.Count);
        for (var i = 0; i < database.Objects.Count; i++)
        {
            indexById[database.Objects[i].ObjectId] = i;
        }

        foreach (var item in database.Objects)
        {
            var chain = new HashSet<ulong>();
            var current = item;
            while (current.ParentId != 0)
            {
                if (!chain.Add(current.ObjectId))
                {
                    throw new DedCorruptException("object graph contains a cycle.");
                }

                if (!byId.TryGetValue(current.ParentId, out var parent))
                {
                    break;
                }

                current = parent;
            }
        }

        var adjacency = new List<int>[database.Objects.Count];
        for (var i = 0; i < database.Objects.Count; i++)
        {
            var item = database.Objects[i];
            var edges = new List<int>(item.ChildIds.Count);
            foreach (var childId in item.ChildIds)
            {
                if (indexById.TryGetValue(childId, out var childIndex))
                {
                    edges.Add(childIndex);
                }
            }

            adjacency[i] = edges;
        }

        var color = new byte[database.Objects.Count];
        for (var start = 0; start < adjacency.Length; start++)
        {
            if (color[start] != 0)
            {
                continue;
            }

            var stack = new Stack<(int Node, int Edge)>();
            stack.Push((start, 0));
            color[start] = 1;
            while (stack.Count > 0)
            {
                var (node, edge) = stack.Pop();
                var edges = adjacency[node];
                if (edge >= edges.Count)
                {
                    color[node] = 2;
                    continue;
                }

                stack.Push((node, edge + 1));
                var next = edges[edge];
                if (color[next] == 1)
                {
                    throw new DedCorruptException("object graph contains a cycle.");
                }

                if (color[next] == 0)
                {
                    color[next] = 1;
                    stack.Push((next, 0));
                }
            }
        }
    }

    private static MemoryStream Buffer(Stream source, DedReadOptions options, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > options.MaxBufferedFileBytes)
            {
                buffer.Dispose();
                throw new DedLimitExceededException("file size", (ulong)total);
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static byte[] ReadAt(Stream stream, long offset, int count)
    {
        if (count < 0 || offset < 0 || (ulong)offset + (ulong)count > (ulong)stream.Length)
        {
            throw new DedCorruptException("read range extends beyond the end of the file.");
        }

        stream.Position = offset;
        var buffer = new byte[count];
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read == 0)
            {
                throw new DedCorruptException("file ended unexpectedly.");
            }

            total += read;
        }

        return buffer;
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24));

    private static ulong ReadUInt64(byte[] buffer, int offset) =>
        ReadUInt32(buffer, offset) | ((ulong)ReadUInt32(buffer, offset + 4) << 32);
}
