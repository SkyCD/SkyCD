using System;

namespace SkyCD.Plugin.Video.Parsing;

/// <summary>
/// Big- and little-endian binary primitives, plus the variable-width integers that EBML uses.
/// </summary>
/// <remarks>
/// Every accessor is bounds checked and returns false rather than throwing, because a truncated or lying
/// header is an ordinary outcome when reading other programs' files. Callers treat a failed read as "this
/// field is absent", which is what the schema's omission semantics already mean.
/// </remarks>
internal static class VideoBinary
{
    public static bool TryU16BE(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        value = 0;
        if (offset < 0 || offset + 2 > data.Length)
        {
            return false;
        }

        value = (ushort)((data[offset] << 8) | data[offset + 1]);
        return true;
    }

    public static bool TryU16LE(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        value = 0;
        if (offset < 0 || offset + 2 > data.Length)
        {
            return false;
        }

        value = (ushort)(data[offset] | (data[offset + 1] << 8));
        return true;
    }

    public static bool TryU32BE(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) | data[offset + 3];
        return true;
    }

    public static bool TryU32LE(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        return true;
    }

    public static bool TryI32LE(ReadOnlySpan<byte> data, int offset, out int value)
    {
        value = 0;
        if (!TryU32LE(data, offset, out var raw))
        {
            return false;
        }

        value = unchecked((int)raw);
        return true;
    }

    public static bool TryU64BE(ReadOnlySpan<byte> data, int offset, out ulong value)
    {
        value = 0;
        if (!TryU32BE(data, offset, out var high) || !TryU32BE(data, offset + 4, out var low))
        {
            return false;
        }

        value = ((ulong)high << 32) | low;
        return true;
    }

    public static bool StartsWith(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> prefix) =>
        offset >= 0 && offset + prefix.Length <= data.Length && data.Slice(offset, prefix.Length).SequenceEqual(prefix);

    public static string Ascii(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        return System.Text.Encoding.ASCII.GetString(data.Slice(offset, length)).TrimEnd('\0').Trim();
    }

    /// <summary>
    /// Reads an EBML element id, which keeps its length bits: the id <c>0x1A45DFA3</c> is four bytes and its
    /// value is the whole pattern, not the payload after masking.
    /// </summary>
    public static bool TryReadEbmlId(ReadOnlySpan<byte> data, int offset, out uint id, out int length)
    {
        id = 0;
        length = 0;

        if (offset < 0 || offset >= data.Length)
        {
            return false;
        }

        var first = data[offset];
        if (first == 0)
        {
            return false;
        }

        var width = LeadingZeroBits(first) + 1;
        if (width > 4 || offset + width > data.Length)
        {
            return false;
        }

        uint value = 0;
        for (var i = 0; i < width; i++)
        {
            value = (value << 8) | data[offset + i];
        }

        id = value;
        length = width;
        return true;
    }

    /// <summary>
    /// Reads an EBML data size. The length bits are masked out, and a size of all ones means "unknown",
    /// which is legal for a live segment and reported as <c>null</c>.
    /// </summary>
    public static bool TryReadEbmlSize(ReadOnlySpan<byte> data, int offset, out long size, out int length)
    {
        size = 0;
        length = 0;

        if (offset < 0 || offset >= data.Length)
        {
            return false;
        }

        var first = data[offset];
        if (first == 0)
        {
            return false;
        }

        var width = LeadingZeroBits(first) + 1;
        if (width > 8 || offset + width > data.Length)
        {
            return false;
        }

        var mask = 0xFFUL >> width;
        var value = (ulong)(first & (byte)mask);
        var allOnes = (first & (byte)mask) == (byte)mask;

        for (var i = 1; i < width; i++)
        {
            value = (value << 8) | data[offset + i];
            allOnes &= data[offset + i] == 0xFF;
        }

        length = width;

        if (allOnes)
        {
            // Unknown size: the element runs to the end of its parent.
            size = -1;
            return true;
        }

        if (value > (ulong)long.MaxValue)
        {
            return false;
        }

        size = (long)value;
        return true;
    }

    /// <summary>Reads an EBML unsigned integer body of up to eight bytes.</summary>
    public static bool TryReadEbmlUInt(ReadOnlySpan<byte> data, int offset, int length, out ulong value)
    {
        value = 0;
        if (offset < 0 || length is < 1 or > 8 || offset + length > data.Length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            value = (value << 8) | data[offset + i];
        }

        return true;
    }

    /// <summary>Reads an EBML IEEE-754 float body of four or eight bytes.</summary>
    public static bool TryReadEbmlFloat(ReadOnlySpan<byte> data, int offset, int length, out double value)
    {
        value = 0;
        if (offset < 0 || offset + length > data.Length)
        {
            return false;
        }

        if (length == 4)
        {
            if (!TryU32BE(data, offset, out var raw))
            {
                return false;
            }

            value = BitConverter.UInt32BitsToSingle(raw);
            return true;
        }

        if (length == 8)
        {
            if (!TryU64BE(data, offset, out var bits))
            {
                return false;
            }

            value = BitConverter.UInt64BitsToDouble(bits);
            return true;
        }

        return false;
    }

    /// <summary>Reads an EBML UTF-8 string body, trimmed of the padding writers add.</summary>
    public static string ReadEbmlString(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        return System.Text.Encoding.UTF8.GetString(data.Slice(offset, length)).TrimEnd('\0').Trim();
    }

    private static int LeadingZeroBits(byte value)
    {
        var count = 0;
        for (var bit = 7; bit >= 0; bit--)
        {
            if ((value & (1 << bit)) != 0)
            {
                return count;
            }

            count++;
        }

        return 8;
    }
}
