using System;
using System.Collections.Generic;
using System.Text;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Minimal reader for the binary property list format (<c>bplist00</c>) macOS uses for Finder metadata. Only
/// arrays of strings and single strings are decoded, which is all those attributes contain. Every offset is
/// bounds-checked, so a damaged attribute yields nothing instead of an exception.
/// </summary>
internal static class BinaryPlistReader
{
    private const int TrailerBytes = 32;
    private const int MaxObjects = 1 << 20;

    private static ReadOnlySpan<byte> Header => "bplist00"u8;

    public static IReadOnlyList<string> ReadStrings(ReadOnlySpan<byte> data)
    {
        var values = new List<string>();
        if (!TryGetHeader(data, out var header) ||
            !TryGetObjectOffset(data, header, header.TopObject, out var top) ||
            !TryReadArray(data, top, out var count, out var contentOffset))
        {
            return values;
        }

        for (var index = 0L; index < count && values.Count < MetadataScan.MaxTagCount; index++)
        {
            var referencePosition = contentOffset + index * header.ObjectRefSize;
            if (!TryReadBigEndian(data, referencePosition, header.ObjectRefSize, out var reference) ||
                !TryGetObjectOffset(data, header, reference, out var itemOffset) ||
                !TryReadString(data, itemOffset, out var text))
            {
                continue;
            }

            values.Add(text);
        }

        return values;
    }

    public static string? ReadString(ReadOnlySpan<byte> data)
    {
        if (!TryGetHeader(data, out var header) ||
            !TryGetObjectOffset(data, header, header.TopObject, out var top))
        {
            return null;
        }

        return TryReadString(data, top, out var text) ? text : null;
    }

    private readonly record struct PlistHeader(int OffsetIntSize, int ObjectRefSize, long NumObjects,
        long TopObject, long OffsetTableOffset);

    private static bool TryGetHeader(ReadOnlySpan<byte> data, out PlistHeader header)
    {
        header = default;
        if (data.Length < Header.Length + TrailerBytes || !data[..Header.Length].SequenceEqual(Header))
        {
            return false;
        }

        var trailer = data[^TrailerBytes..];
        int offsetIntSize = trailer[6];
        int objectRefSize = trailer[7];
        if (offsetIntSize is < 1 or > 8 || objectRefSize is < 1 or > 8)
        {
            return false;
        }

        var numObjects = ReadBigEndian(trailer[8..16]);
        var topObject = ReadBigEndian(trailer[16..24]);
        var offsetTableOffset = ReadBigEndian(trailer[24..32]);
        var tableEnd = offsetTableOffset + numObjects * offsetIntSize;
        if (numObjects is <= 0 or > MaxObjects || topObject < 0 || topObject >= numObjects ||
            offsetTableOffset <= 0 || tableEnd < offsetTableOffset || tableEnd > data.Length - TrailerBytes)
        {
            return false;
        }

        header = new PlistHeader(offsetIntSize, objectRefSize, numObjects, topObject, offsetTableOffset);
        return true;
    }

    private static bool TryGetObjectOffset(ReadOnlySpan<byte> data, in PlistHeader header, long reference,
        out int offset)
    {
        offset = 0;
        if (reference < 0 || reference >= header.NumObjects ||
            !TryReadBigEndian(data, header.OffsetTableOffset + reference * header.OffsetIntSize,
                header.OffsetIntSize, out var value) ||
            value < 0 || value >= header.OffsetTableOffset)
        {
            return false;
        }

        offset = (int)value;
        return true;
    }

    private static bool TryReadArray(ReadOnlySpan<byte> data, int offset, out long count, out long contentOffset) =>
        TryReadCount(data, offset, 0xA0, out count, out contentOffset);

    private static bool TryReadString(ReadOnlySpan<byte> data, int offset, out string text)
    {
        text = string.Empty;
        if (offset < 0 || offset >= data.Length)
        {
            return false;
        }

        var marker = (byte)(data[offset] & 0xF0);
        if (marker is not (0x50 or 0x60) ||
            !TryReadCount(data, offset, marker, out var count, out var contentOffset))
        {
            return false;
        }

        // UTF-16 strings store one code unit per counted element.
        var byteCount = marker == 0x60 ? count * 2 : count;
        if (byteCount < 0 || byteCount > int.MaxValue || contentOffset + byteCount > data.Length)
        {
            return false;
        }

        var bytes = data.Slice((int)contentOffset, (int)byteCount);
        text = StripColorSuffix(MetadataText.Clean(marker == 0x60
            ? Encoding.BigEndianUnicode.GetString(bytes)
            : Encoding.Latin1.GetString(bytes)));
        return text.Length > 0;
    }

    /// <summary>Finder appends a line break and a colour number to every tag; the name is what the user typed.</summary>
    private static string StripColorSuffix(string value)
    {
        var separator = value.IndexOf('\n');
        return separator < 0 ? value : value[..separator].Trim();
    }

    /// <summary>
    /// Reads a counted object (array or string) at <paramref name="offset"/>: the low nibble is the count, or
    /// <c>0xF</c> followed by an integer object holding a larger count.
    /// </summary>
    private static bool TryReadCount(ReadOnlySpan<byte> data, int offset, byte markerHigh, out long count,
        out long contentOffset)
    {
        count = 0;
        contentOffset = 0;
        if (offset < 0 || offset >= data.Length || (data[offset] & 0xF0) != markerHigh)
        {
            return false;
        }

        var length = data[offset] & 0x0F;
        if (length != 0x0F)
        {
            count = length;
            contentOffset = offset + 1L;
            return true;
        }

        if (!TryReadInteger(data, offset + 1, out var extended, out var nextOffset))
        {
            return false;
        }

        count = extended;
        contentOffset = nextOffset;
        return count >= 0;
    }

    private static bool TryReadInteger(ReadOnlySpan<byte> data, int offset, out long value, out int nextOffset)
    {
        value = 0;
        nextOffset = 0;
        if (offset < 0 || offset >= data.Length || (data[offset] & 0xF0) != 0x10)
        {
            return false;
        }

        var size = 1 << (data[offset] & 0x0F);
        if (size is < 1 or > 8 || offset + 1 + size > data.Length)
        {
            return false;
        }

        value = ReadBigEndian(data.Slice(offset + 1, size));
        nextOffset = offset + 1 + size;
        return true;
    }

    private static bool TryReadBigEndian(ReadOnlySpan<byte> data, long offset, int size, out long value)
    {
        value = 0;
        if (offset < 0 || size < 1 || size > 8 || offset + size > data.Length)
        {
            return false;
        }

        value = ReadBigEndian(data.Slice((int)offset, size));
        return true;
    }

    private static long ReadBigEndian(ReadOnlySpan<byte> value)
    {
        long result = 0;
        foreach (var item in value)
        {
            result = (result << 8) | item;
        }

        return result;
    }
}
