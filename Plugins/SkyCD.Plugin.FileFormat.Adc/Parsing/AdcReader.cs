using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.FileFormat.Adc.Exceptions;
using SkyCD.Plugin.FileFormat.Adc.Models;

namespace SkyCD.Plugin.FileFormat.Adc.Parsing;

/// <summary>
/// Reads an Advanced Disk Catalog database from a stream.
/// </summary>
/// <remarks>
/// The format is documented in <c>FORMAT.md</c>. Every read is bounded by the record frame
/// and by the configured safety limits, so malformed length prefixes cannot cause unbounded
/// allocation. The hierarchy is validated after the record stream is read: unknown parents,
/// duplicate identifiers, parent cycles and excessive nesting are reported as dedicated errors,
/// never as a partially imported catalog.
/// </remarks>
public static class AdcReader
{
    private static readonly UnicodeEncoding StrictUtf16 =
        new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    public static AdcCatalog Read(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var data = ReadAllBytesBounded(source, cancellationToken);
        if (AdcDetector.IsAuthenticodeCatalog(data))
        {
            throw new AdcAuthenticodeCatalogException();
        }

        if (data.Length < AdcFormat.HeaderSize)
        {
            throw new AdcTruncatedRecordException(0, "file header");
        }

        var header = ReadHeader(data, cancellationToken);
        var catalog = new AdcCatalog
        {
            VersionMajor = header.Major,
            VersionMinor = header.Minor,
            Flags = header.Flags,
            TotalRecordCount = (int)header.RecordCount
        };

        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        var volumesById = new Dictionary<uint, AdcVolume>();
        var itemsById = new Dictionary<uint, AdcItem>();
        var categoriesById = new Dictionary<uint, AdcCategory>();
        var position = header.BodyOffset;

        for (var recordIndex = 0u; recordIndex < header.RecordCount; recordIndex++)
        {
            if (recordIndex % AdcFormat.CancelCheckEveryRecords == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var index = (int)recordIndex;
            EnsureRemaining(data, position, AdcFormat.RecordHeaderSize, header.CommentTableOffset, index, "record header");
            var recordType = ReadUInt32(data, position);
            var payloadLength = ReadUInt32(data, position + 4);
            position += AdcFormat.RecordHeaderSize;
            var remaining = header.CommentTableOffset - position;
            if (payloadLength > (uint)remaining)
            {
                throw new AdcTruncatedRecordException(index, $"record 0x{recordType:X8} payload");
            }

            var payloadEnd = position + (int)payloadLength;
            switch (recordType)
            {
                case AdcFormat.RecordTypeVolume:
                    var volume = ReadVolume(data, ref position, payloadEnd, index, unsupported);
                    if (!volumesById.TryAdd(volume.VolumeId, volume))
                    {
                        throw new AdcDuplicateIdException("volume", volume.VolumeId);
                    }

                    catalog.Volumes.Add(volume);
                    break;
                case AdcFormat.RecordTypeItem:
                    var item = ReadItem(data, ref position, payloadEnd, index, unsupported);
                    if (!itemsById.TryAdd(item.ItemId, item))
                    {
                        throw new AdcDuplicateIdException("item", item.ItemId);
                    }

                    catalog.Items.Add(item);
                    break;
                case AdcFormat.RecordTypeCategory:
                    var category = ReadCategory(data, ref position, payloadEnd, index);
                    if (!categoriesById.TryAdd(category.CategoryId, category))
                    {
                        throw new AdcDuplicateIdException("category", category.CategoryId);
                    }

                    catalog.Categories.Add(category);
                    break;
                default:
                    unsupported.Add($"record:0x{recordType:X8}");
                    position = payloadEnd;
                    break;
            }

            if (position != payloadEnd)
            {
                throw new AdcCorruptException($"record {index} has {payloadEnd - position} unread payload bytes.");
            }
        }

        if (position != header.CommentTableOffset)
        {
            throw new AdcCorruptException(
                $"record count {header.RecordCount} does not match the body length.");
        }

        if (catalog.Categories.Count != header.CategoryCount)
        {
            throw new AdcCorruptException(
                $"category count {header.CategoryCount} does not match the {catalog.Categories.Count} category records.");
        }

        catalog.Comments.AddRange(ReadComments(data, header));
        ValidateReferences(catalog, volumesById, itemsById, categoriesById);
        catalog.UnsupportedFields.AddRange(unsupported);
        return catalog;
    }

    private static Header ReadHeader(byte[] data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ReadUInt32(data, 0) != AdcFormat.SignatureLength)
        {
            throw new AdcCorruptException("missing the ADC.Catalog header length.");
        }

        for (var i = 0; i < AdcFormat.Signature.Length; i++)
        {
            if (data[AdcFormat.SignatureOffset + i] != AdcFormat.Signature[i])
            {
                throw new AdcCorruptException("missing the ADC.Catalog signature.");
            }
        }

        var major = ReadUInt16(data, AdcFormat.VersionMajorOffset);
        var minor = ReadUInt16(data, AdcFormat.VersionMinorOffset);
        if (major != AdcFormat.SupportedVersionMajor)
        {
            throw new AdcUnsupportedVersionException(major, minor);
        }

        var flags = ReadUInt32(data, AdcFormat.FlagsOffset);
        var recordCount = ReadUInt32(data, AdcFormat.RecordCountOffset);
        if (recordCount > AdcFormat.MaxRecordCount)
        {
            throw new AdcLimitExceededException("record count", recordCount, AdcFormat.MaxRecordCount);
        }

        var bodyOffset = ReadUInt64(data, AdcFormat.BodyOffsetOffset);
        if (bodyOffset < AdcFormat.HeaderSize || bodyOffset > (ulong)data.Length)
        {
            throw new AdcMalformedOffsetException("body", (long)bodyOffset);
        }

        var commentTableOffset = ReadUInt64(data, AdcFormat.CommentTableOffsetOffset);
        if (commentTableOffset < bodyOffset || commentTableOffset > (ulong)data.Length)
        {
            throw new AdcMalformedOffsetException("comment table", (long)commentTableOffset);
        }

        var commentCount = ReadUInt32(data, AdcFormat.CommentCountOffset);
        if (commentCount > AdcFormat.MaxRecordCount)
        {
            throw new AdcLimitExceededException("comment count", commentCount, AdcFormat.MaxRecordCount);
        }

        var categoryCount = ReadUInt32(data, AdcFormat.CategoryCountOffset);
        if (categoryCount > AdcFormat.MaxRecordCount)
        {
            throw new AdcLimitExceededException("category count", categoryCount, AdcFormat.MaxRecordCount);
        }

        return new Header(
            major,
            minor,
            flags,
            recordCount,
            (int)bodyOffset,
            (int)commentTableOffset,
            commentCount,
            categoryCount);
    }

    private static AdcVolume ReadVolume(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        SortedSet<string> unsupported)
    {
        var volume = new AdcVolume
        {
            VolumeId = ReadUInt32(data, ref position, limit, recordIndex, "volume id")
        };
        volume.VolumeType = (AdcVolumeType)ReadByte(data, ref position, limit, recordIndex, "volume type");
        volume.Flags = ReadByte(data, ref position, limit, recordIndex, "volume flags");
        _ = ReadUInt16(data, ref position, limit, recordIndex, "volume reserved field");
        volume.SerialNumber = ReadUInt32(data, ref position, limit, recordIndex, "volume serial number");
        volume.CapacityBytes = ReadUInt64(data, ref position, limit, recordIndex, "volume capacity");
        volume.Label = ReadUtf16String(data, ref position, limit, recordIndex, "volume label");
        volume.Source = ReadUtf16String(data, ref position, limit, recordIndex, "volume source");
        ReadProperties(data, ref position, limit, recordIndex, volume.Properties, unsupported);
        return volume;
    }

    private static AdcItem ReadItem(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        SortedSet<string> unsupported)
    {
        var item = new AdcItem
        {
            ItemId = ReadUInt32(data, ref position, limit, recordIndex, "item id"),
            ParentId = ReadUInt32(data, ref position, limit, recordIndex, "item parent id"),
            VolumeId = ReadUInt32(data, ref position, limit, recordIndex, "item volume id")
        };
        var kind = ReadByte(data, ref position, limit, recordIndex, "item kind");
        if (kind > (byte)AdcItemKind.Folder)
        {
            throw new AdcCorruptException($"item {item.ItemId} has unknown kind {kind}.");
        }

        item.Kind = (AdcItemKind)kind;
        item.Attributes = ReadByte(data, ref position, limit, recordIndex, "item attributes");
        _ = ReadUInt16(data, ref position, limit, recordIndex, "item reserved field");
        item.TypeCode = ReadUInt32(data, ref position, limit, recordIndex, "item type code");
        item.SizeBytes = ReadUInt64(data, ref position, limit, recordIndex, "item size");
        item.CreatedFileTime = ReadUInt64(data, ref position, limit, recordIndex, "item created time");
        item.ModifiedFileTime = ReadUInt64(data, ref position, limit, recordIndex, "item modified time");
        item.AccessedFileTime = ReadUInt64(data, ref position, limit, recordIndex, "item accessed time");
        item.CommentIndex = ReadUInt32(data, ref position, limit, recordIndex, "item comment index");

        var categoryCount = ReadUInt16(data, ref position, limit, recordIndex, "item category count");
        if (categoryCount > AdcFormat.MaxCategoriesPerItem)
        {
            throw new AdcLimitExceededException(
                "category count per item", categoryCount, AdcFormat.MaxCategoriesPerItem);
        }

        for (var i = 0; i < categoryCount; i++)
        {
            item.CategoryIds.Add(ReadUInt32(data, ref position, limit, recordIndex, "item category id"));
        }

        item.Name = ReadUtf16String(data, ref position, limit, recordIndex, "item name");
        if (item.Name.Length == 0)
        {
            throw new AdcCorruptException($"item {item.ItemId} has an empty name.");
        }

        ReadProperties(data, ref position, limit, recordIndex, item.Properties, unsupported);
        ValidateFileTime(item.CreatedFileTime, item.ItemId, "created");
        ValidateFileTime(item.ModifiedFileTime, item.ItemId, "modified");
        ValidateFileTime(item.AccessedFileTime, item.ItemId, "accessed");
        return item;
    }

    private static AdcCategory ReadCategory(byte[] data, ref int position, int limit, int recordIndex)
    {
        var category = new AdcCategory
        {
            CategoryId = ReadUInt32(data, ref position, limit, recordIndex, "category id")
        };
        category.Name = ReadUtf16String(data, ref position, limit, recordIndex, "category name");
        if (category.Name.Length == 0)
        {
            throw new AdcCorruptException($"category {category.CategoryId} has an empty name.");
        }

        return category;
    }

    private static void ReadProperties(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        List<AdcProperty> target,
        SortedSet<string> unsupported)
    {
        var count = ReadUInt16(data, ref position, limit, recordIndex, "property count");
        if (count > AdcFormat.MaxPropertyCount)
        {
            throw new AdcLimitExceededException("property count", count, AdcFormat.MaxPropertyCount);
        }

        for (var i = 0; i < count; i++)
        {
            var descriptor = ReadByte(data, ref position, limit, recordIndex, "property descriptor");
            var id = (byte)(descriptor & 0x3F);
            var kind = (descriptor >> 6) & 0x03;
            switch (kind)
            {
                case 0:
                    target.Add(new AdcProperty(id, ReadUInt32(data, ref position, limit, recordIndex, "property value")));
                    break;
                case 1:
                    target.Add(new AdcProperty(id, ReadUtf16String(data, ref position, limit, recordIndex, "property text")));
                    break;
                case 2:
                    var byteLength = ReadUInt32(data, ref position, limit, recordIndex, "property blob length");
                    if (byteLength > AdcFormat.MaxBlobBytes)
                    {
                        throw new AdcLimitExceededException("property blob length", byteLength, AdcFormat.MaxBlobBytes);
                    }

                    var blobLength = (int)byteLength;
                    EnsureRemaining(data, position, blobLength, limit, recordIndex, "property blob");
                    var blob = new byte[blobLength];
                    Buffer.BlockCopy(data, position, blob, 0, blobLength);
                    position += blobLength;
                    target.Add(new AdcProperty(id, blob));
                    break;
                default:
                    throw new AdcCorruptException(
                        $"property descriptor 0x{descriptor:X2} uses the reserved encoding.");
            }

            unsupported.Add($"property:0x{descriptor:X2}");
        }
    }

    private static List<string> ReadComments(byte[] data, Header header)
    {
        var comments = new List<string>((int)header.CommentCount);
        var tableStart = header.CommentTableOffset;
        var remaining = data.Length - tableStart;
        var recordIndex = (int)header.RecordCount;

        if (remaining < 4)
        {
            throw new AdcTruncatedRecordException(recordIndex, "comment table header");
        }

        var count = ReadUInt32(data, tableStart);
        if (count != header.CommentCount)
        {
            throw new AdcCorruptException("comment table count does not match the header.");
        }

        var entryArrayBytes = 4L + ((long)count * 8);
        if (entryArrayBytes > remaining)
        {
            throw new AdcTruncatedRecordException(recordIndex, "comment table entries");
        }

        var entries = new (uint Offset, uint Length)[count];
        var position = tableStart + 4;
        for (var i = 0u; i < count; i++)
        {
            var offset = ReadUInt32(data, position);
            var length = ReadUInt32(data, position + 4);
            position += 8;

            if (offset < entryArrayBytes || offset > int.MaxValue ||
                (ulong)offset + length > (ulong)remaining)
            {
                throw new AdcMalformedOffsetException("comment entry", offset);
            }

            if ((offset & 1) != 0 || (length & 1) != 0)
            {
                throw new AdcCorruptException("comment entry has an odd byte length or offset.");
            }

            if (length / 2 > AdcFormat.MaxStringChars)
            {
                throw new AdcLimitExceededException(
                    "comment length", length / 2, AdcFormat.MaxStringChars);
            }

            entries[i] = (offset, length);
        }

        foreach (var (offset, length) in entries)
        {
            comments.Add(DecodeUtf16(data, tableStart + (int)offset, (int)length, "comment"));
        }

        return comments;
    }

    private static void ValidateReferences(
        AdcCatalog catalog,
        Dictionary<uint, AdcVolume> volumesById,
        Dictionary<uint, AdcItem> itemsById,
        Dictionary<uint, AdcCategory> categoriesById)
    {
        foreach (var item in catalog.Items)
        {
            if (!volumesById.ContainsKey(item.VolumeId))
            {
                throw new AdcCorruptException(
                    $"item {item.ItemId} references unknown volume {item.VolumeId}.");
            }

            if (item.CommentIndex > (uint)catalog.Comments.Count)
            {
                throw new AdcCorruptException(
                    $"item {item.ItemId} references unknown comment {item.CommentIndex}.");
            }

            foreach (var categoryId in item.CategoryIds)
            {
                if (!categoriesById.ContainsKey(categoryId))
                {
                    throw new AdcCorruptException(
                        $"item {item.ItemId} references unknown category {categoryId}.");
                }
            }

            if (item.ParentId == 0)
            {
                continue;
            }

            if (!itemsById.TryGetValue(item.ParentId, out var parent))
            {
                throw new AdcCorruptException(
                    $"item {item.ItemId} references unknown parent {item.ParentId}.");
            }

            if (parent.Kind != AdcItemKind.Folder)
            {
                throw new AdcCorruptException(
                    $"item {item.ItemId} parent {item.ParentId} is not a folder.");
            }

            if (parent.VolumeId != item.VolumeId)
            {
                throw new AdcCorruptException(
                    $"item {item.ItemId} parent {item.ParentId} belongs to another volume.");
            }
        }

        DetectCycles(catalog.Items, itemsById);
    }

    private static void DetectCycles(IReadOnlyList<AdcItem> items, Dictionary<uint, AdcItem> itemsById)
    {
        var depth = new Dictionary<uint, int>(items.Count);
        foreach (var item in items)
        {
            if (depth.ContainsKey(item.ItemId))
            {
                continue;
            }

            var path = new List<AdcItem>();
            var seen = new HashSet<uint>();
            var current = item;
            while (true)
            {
                if (depth.TryGetValue(current.ItemId, out _))
                {
                    break;
                }

                if (!seen.Add(current.ItemId))
                {
                    throw new AdcCycleException(current.ItemId);
                }

                path.Add(current);
                if (current.ParentId == 0)
                {
                    break;
                }

                current = itemsById[current.ParentId];
            }

            var ancestorDepth = depth.TryGetValue(current.ItemId, out var known) ? known : 0;
            for (var i = path.Count - 1; i >= 0; i--)
            {
                ancestorDepth++;
                depth[path[i].ItemId] = ancestorDepth;
                if (ancestorDepth > AdcFormat.MaxItemDepth)
                {
                    throw new AdcLimitExceededException(
                        "item nesting depth", ancestorDepth, AdcFormat.MaxItemDepth);
                }
            }
        }
    }

    private static void ValidateFileTime(ulong value, uint itemId, string field)
    {
        if (value == 0)
        {
            return;
        }

        try
        {
            _ = DateTime.FromFileTimeUtc((long)value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new AdcCorruptException($"item {itemId} has an invalid {field} timestamp.");
        }
    }

    private static string ReadUtf16String(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        string what)
    {
        var charCount = ReadUInt32(data, ref position, limit, recordIndex, $"{what} length");
        if (charCount > AdcFormat.MaxStringChars)
        {
            throw new AdcLimitExceededException($"{what} length", charCount, AdcFormat.MaxStringChars);
        }

        var byteCount = (int)charCount * 2;
        EnsureRemaining(data, position, byteCount, limit, recordIndex, what);
        var text = DecodeUtf16(data, position, byteCount, what);
        position += byteCount;
        return text;
    }

    private static string DecodeUtf16(byte[] data, int offset, int byteLength, string what)
    {
        try
        {
            return StrictUtf16.GetString(data, offset, byteLength);
        }
        catch (DecoderFallbackException)
        {
            throw new AdcCorruptException($"{what} is not valid UTF-16.");
        }
    }

    private static void EnsureRemaining(
        byte[] data,
        int position,
        int needed,
        int limit,
        int recordIndex,
        string what)
    {
        if (needed < 0 || position < 0 || limit < position ||
            limit > data.Length || needed > limit - position)
        {
            throw new AdcTruncatedRecordException(recordIndex, what);
        }
    }

    private static byte ReadByte(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        string what)
    {
        EnsureRemaining(data, position, 1, limit, recordIndex, what);
        return data[position++];
    }

    private static ushort ReadUInt16(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        string what)
    {
        EnsureRemaining(data, position, 2, limit, recordIndex, what);
        var value = (ushort)(data[position] | (data[position + 1] << 8));
        position += 2;
        return value;
    }

    private static uint ReadUInt32(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        string what)
    {
        EnsureRemaining(data, position, 4, limit, recordIndex, what);
        var value = ReadUInt32(data, position);
        position += 4;
        return value;
    }

    private static ulong ReadUInt64(
        byte[] data,
        ref int position,
        int limit,
        int recordIndex,
        string what)
    {
        EnsureRemaining(data, position, 8, limit, recordIndex, what);
        var value = ReadUInt64(data, position);
        position += 8;
        return value;
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] |
               (data[offset + 1] << 8) |
               (data[offset + 2] << 16) |
               (data[offset + 3] << 24));

    private static ulong ReadUInt64(byte[] data, int offset) =>
        ReadUInt32(data, offset) | ((ulong)ReadUInt32(data, offset + 4) << 32);

    private static byte[] ReadAllBytesBounded(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > AdcFormat.MaxFileBytes)
        {
            throw new AdcLimitExceededException(
                "file size", source.Length - source.Position, AdcFormat.MaxFileBytes);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        long total = 0;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > AdcFormat.MaxFileBytes)
            {
                throw new AdcLimitExceededException("file size", total, AdcFormat.MaxFileBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private readonly record struct Header(
        ushort Major,
        ushort Minor,
        uint Flags,
        uint RecordCount,
        int BodyOffset,
        int CommentTableOffset,
        uint CommentCount,
        uint CategoryCount);
}
