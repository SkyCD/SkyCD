using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Import;

/// <summary>
/// Copies selected volume subtrees between databases with optional omission of
/// stored contents, thumbnails, and generated descriptions. Sources are never
/// modified; all returned databases own their object lists.
/// </summary>
public static class DedVolumeTransfer
{
    /// <summary>Copies the complete subtrees of the selected volume ids.</summary>
    public static DedDatabase SelectVolumes(
        DedDatabase source,
        IEnumerable<ulong> volumeIds,
        DedTransferOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(volumeIds);
        options ??= DedTransferOptions.All;

        var selected = new List<ulong>();
        var seen = new HashSet<ulong>();
        foreach (var volumeId in volumeIds)
        {
            if (!seen.Add(volumeId))
            {
                continue;
            }

            var item = source.Find(volumeId)
                ?? throw new DedTransferException("a selected volume does not exist in the source database.");
            if (item.Kind != DedObjectKind.Volume)
            {
                throw new DedTransferException("a selected object is not a volume.");
            }

            selected.Add(volumeId);
        }

        var children = BuildChildrenMap(source);
        var included = new HashSet<ulong>();
        var pending = new Stack<ulong>();
        foreach (var volumeId in selected)
        {
            pending.Push(volumeId);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = pending.Pop();
            if (!included.Add(id))
            {
                continue;
            }

            if (children.TryGetValue(id, out var childIds))
            {
                foreach (var childId in childIds)
                {
                    pending.Push(childId);
                }
            }
        }

        var result = CopyHeader(source);
        foreach (var item in source.Objects)
        {
            if (!included.Contains(item.ObjectId))
            {
                continue;
            }

            var clone = Clone(item, options);
            clone.ParentId = included.Contains(item.ParentId) ? item.ParentId : 0;
            clone.ChildIds.Clear();
            if (children.TryGetValue(item.ObjectId, out var childList))
            {
                foreach (var childId in childList)
                {
                    if (included.Contains(childId))
                    {
                        clone.ChildIds.Add(childId);
                    }
                }
            }

            if (clone.RawRecord is { Length: >= 20 } rawRecord)
            {
                var embeddedParent = ReadUInt64(rawRecord, 20);
                if (embeddedParent != 0 && !included.Contains(embeddedParent))
                {
                    throw new DedTransferException("an opaque object references data outside the selection.");
                }
            }

            result.Objects.Add(clone);
        }

        result.TableSlotCount = 0;
        return result;
    }

    /// <summary>Copies an entire database, applying payload omissions.</summary>
    public static DedDatabase Copy(DedDatabase source, DedTransferOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= DedTransferOptions.All;

        var result = CopyHeader(source);
        var children = BuildChildrenMap(source);
        foreach (var item in source.Objects)
        {
            var clone = Clone(item, options);
            clone.ChildIds.Clear();
            if (children.TryGetValue(item.ObjectId, out var childList))
            {
                clone.ChildIds.AddRange(childList);
            }

            result.Objects.Add(clone);
        }

        return result;
    }

    /// <summary>Merges an incoming database into a copy of an existing database.</summary>
    public static DedDatabase Merge(DedDatabase existing, DedDatabase incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var result = CopyHeader(existing);
        var usedIds = new HashSet<ulong>();
        ulong nextId = 0;
        foreach (var item in existing.Objects)
        {
            usedIds.Add(item.ObjectId);
            nextId = Math.Max(nextId, item.ObjectId);
            result.Objects.Add(Clone(item, DedTransferOptions.All));
        }

        var remap = new Dictionary<ulong, ulong>(incoming.Objects.Count);
        foreach (var item in incoming.Objects)
        {
            if (item.RawRecord is not null)
            {
                throw new DedTransferException(
                    "opaque object records cannot be merged into an existing database.");
            }

            var assigned = nextId + 1;
            while (!usedIds.Add(assigned))
            {
                assigned++;
            }

            nextId = assigned;
            remap[item.ObjectId] = assigned;
        }

        foreach (var item in incoming.Objects)
        {
            var clone = Clone(item, DedTransferOptions.All);
            clone.ObjectId = remap[item.ObjectId];
            clone.ParentId = item.ParentId != 0 && remap.TryGetValue(item.ParentId, out var parentId)
                ? parentId
                : 0;
            clone.ChildIds.Clear();
            foreach (var childId in item.ChildIds)
            {
                if (remap.TryGetValue(childId, out var remapped))
                {
                    clone.ChildIds.Add(remapped);
                }
            }

            clone.CategoryIds.Clear();
            foreach (var categoryId in item.CategoryIds)
            {
                if (remap.TryGetValue(categoryId, out var remapped))
                {
                    clone.CategoryIds.Add(remapped);
                }
            }

            result.Objects.Add(clone);
        }

        return result;
    }

    private static DedDatabase CopyHeader(DedDatabase source) => new()
    {
        FormatVersion = source.FormatVersion,
        HeaderFlags = source.HeaderFlags,
        ReservedFlags = source.ReservedFlags,
        CreationFileTime = source.CreationFileTime,
        Title = source.Title,
        TableSlotCount = source.TableSlotCount
    };

    private static DedCatalogObject Clone(DedCatalogObject source, DedTransferOptions options)
    {
        var clone = new DedCatalogObject
        {
            RawKind = source.RawKind,
            ObjectId = source.ObjectId,
            ParentId = source.ParentId,
            Flags = source.Flags,
            Name = source.Name,
            Path = source.Path,
            Description = source.Description,
            LogicalSize = source.LogicalSize,
            AggregateSize = source.AggregateSize,
            CreatedFileTime = source.CreatedFileTime,
            ModifiedFileTime = source.ModifiedFileTime,
            AddedFileTime = source.AddedFileTime,
            FileCount = source.FileCount,
            FolderCount = source.FolderCount,
            Tail = source.Tail,
            RawRecord = source.RawRecord
        };

        clone.ChildIds.AddRange(source.ChildIds);
        clone.CategoryIds.AddRange(source.CategoryIds);
        foreach (var extension in source.Extensions)
        {
            clone.Extensions.Add(new DedExtensionField(extension.Tag, extension.Data));
        }

        foreach (var payload in source.Payloads)
        {
            var kind = payload.Kind;
            if (kind != DedPayloadKind.Unknown && !options.Allows(kind))
            {
                continue;
            }

            clone.Payloads.Add(new DedPayload(payload.RawKind, payload.Data));
        }

        return clone;
    }

    private static ulong ReadUInt64(byte[] buffer, int offset) =>
        (uint)(buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24))
        | ((ulong)(uint)(buffer[offset + 4]
            | (buffer[offset + 5] << 8)
            | (buffer[offset + 6] << 16)
            | (buffer[offset + 7] << 24)) << 32);

    private static Dictionary<ulong, List<ulong>> BuildChildrenMap(DedDatabase database)
    {
        var existing = new HashSet<ulong>();
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
}
