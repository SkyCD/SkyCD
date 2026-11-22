using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Export;

/// <summary>
/// Writes a deterministic DED v0x102 container. Object records are emitted in
/// model order with recomputed offsets and lengths; unknown records, flags,
/// extension fields, and payload bytes are copied verbatim. File writes use a
/// temporary sibling and replace the destination only after the whole file has
/// been serialized.
/// </summary>
public static class DedWriter
{
    /// <summary>Serializes a database to a stream. The stream is left open.</summary>
    public static void Write(Stream target, DedDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(database);

        var records = Prepare(database);
        var slotCount = ResolveSlotCount(database, records.Count);
        var tableBytes = (ulong)slotCount * 4UL;
        var tableEnd = (ulong)DedFormat.HeaderSize + tableBytes;
        if (tableEnd > DedFormat.MaximumFileSize)
        {
            throw new DedModelException("offset table exceeds the 32-bit file address space.");
        }

        var offsets = new uint[records.Count];
        var position = tableEnd;
        for (var i = 0; i < records.Count; i++)
        {
            if (position > DedFormat.MaximumFileSize)
            {
                throw new DedModelException("serialized database exceeds the 32-bit file address space.");
            }

            offsets[i] = (uint)position;
            position += (ulong)records[i].Length;
        }

        if (position > DedFormat.MaximumFileSize)
        {
            throw new DedModelException("serialized database exceeds the 32-bit file address space.");
        }

        var header = BuildHeader(database, (uint)slotCount, (uint)records.Count);
        target.Write(header, 0, header.Length);

        var table = new byte[slotCount * 4];
        for (var i = 0; i < offsets.Length; i++)
        {
            var offset = offsets[i];
            table[i * 4] = (byte)offset;
            table[(i * 4) + 1] = (byte)(offset >> 8);
            table[(i * 4) + 2] = (byte)(offset >> 16);
            table[(i * 4) + 3] = (byte)(offset >> 24);
        }

        target.Write(table, 0, table.Length);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.Write(record, 0, record.Length);
        }
    }

    /// <summary>
    /// Serializes to a temporary sibling and atomically replaces the destination
    /// only after every offset and length has been finalized.
    /// </summary>
    public static void WriteFile(string path, DedDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(database);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new DedModelException("target path has no directory.");
        }

        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Path.GetFileName(fullPath) + ".tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.SequentialScan))
            {
                Write(stream, database, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
            {
                File.Replace(temporary, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, fullPath);
            }
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static List<byte[]> Prepare(DedDatabase database)
    {
        if (database.FormatVersion != DedFormat.CurrentVersion)
        {
            throw new DedUnsupportedVersionException(database.FormatVersion);
        }

        _ = DedTextCodec.EncodeHeaderTitle(database.Title);

        var byId = new Dictionary<ulong, DedCatalogObject>(database.Objects.Count);
        foreach (var item in database.Objects)
        {
            if (item.ObjectId == 0)
            {
                throw new DedModelException("object id zero is reserved.");
            }

            if (!byId.TryAdd(item.ObjectId, item))
            {
                throw new DedModelException("object ids are not unique.");
            }
        }

        foreach (var item in database.Objects)
        {
            if (item.ParentId != 0 && !byId.ContainsKey(item.ParentId))
            {
                throw new DedModelException("object references a missing parent.");
            }

            foreach (var childId in item.ChildIds)
            {
                if (!byId.TryGetValue(childId, out var child))
                {
                    throw new DedModelException("object references a missing child.");
                }

                if (child.ParentId != item.ObjectId)
                {
                    throw new DedModelException("child parent ids do not match their owning object.");
                }
            }
        }

        DetectCycles(database, byId);

        var records = new List<byte[]>(database.Objects.Count);
        foreach (var item in database.Objects)
        {
            if (item.IsUnknownKind)
            {
                if (item.RawRecord is null || item.RawRecord.Length < 20)
                {
                    throw new DedModelException("unknown object kind has no preserved raw record.");
                }

                records.Add(item.RawRecord);
            }
            else
            {
                records.Add(DedRecordSerializer.Serialize(item));
            }
        }

        return records;
    }

    private static int ResolveSlotCount(DedDatabase database, int objectCount)
    {
        var slots = Math.Max(DedFormat.InitialTableSlots, Math.Max(database.TableSlotCount, objectCount));
        if ((ulong)slots * 4UL + DedFormat.HeaderSize > DedFormat.MaximumFileSize)
        {
            throw new DedModelException("offset table exceeds the 32-bit file address space.");
        }

        return slots;
    }

    private static byte[] BuildHeader(DedDatabase database, uint slotCount, uint populatedCount)
    {
        var header = new byte[DedFormat.HeaderSize];
        DedFormat.Magic.CopyTo(header);
        WriteUInt32(header, 0x04, DedFormat.CurrentVersion);
        WriteUInt32(header, 0x08, slotCount);
        WriteUInt32(header, 0x0C, populatedCount);
        WriteUInt32(header, DedFormat.FlagsOffset, database.HeaderFlags);
        WriteUInt64(header, DedFormat.CreationTimeOffset, database.CreationFileTime);
        WriteUInt32(header, DedFormat.ReservedFlagsOffset, database.ReservedFlags);
        var title = DedTextCodec.EncodeHeaderTitle(database.Title);
        Array.Copy(title, 0, header, DedFormat.TitleOffset, DedFormat.TitleSize);
        return header;
    }

    private static void DetectCycles(DedDatabase database, Dictionary<ulong, DedCatalogObject> byId)
    {
        var indexById = new Dictionary<ulong, int>(byId.Count);
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
                    throw new DedModelException("object graph contains a cycle.");
                }

                current = byId[current.ParentId];
            }
        }

        var adjacency = new List<int>[database.Objects.Count];
        for (var i = 0; i < database.Objects.Count; i++)
        {
            var item = database.Objects[i];
            var edges = new List<int>(item.ChildIds.Count);
            foreach (var childId in item.ChildIds)
            {
                edges.Add(indexById[childId]);
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
                    throw new DedModelException("object graph contains a cycle.");
                }

                if (color[next] == 0)
                {
                    color[next] = 1;
                    stack.Push((next, 0));
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt64(byte[] buffer, int offset, ulong value)
    {
        WriteUInt32(buffer, offset, (uint)value);
        WriteUInt32(buffer, offset + 4, (uint)(value >> 32));
    }
}
