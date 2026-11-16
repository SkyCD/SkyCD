using System;

namespace SkyCD.Plugin.Audio;

/// <summary>
/// Big- and little-endian binary primitives plus the text decoding that audio tag formats need.
/// </summary>
/// <remarks>
/// Every accessor is bounds checked and returns false rather than throwing, because a truncated or lying
/// header is an ordinary outcome when reading other programs' files. Callers treat a failed read as "this
/// field is absent", which is what the schema's omission semantics already mean.
/// </remarks>
internal static class AudioBinary
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

    public static bool TryU24BE(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 3 > data.Length)
        {
            return false;
        }

        value = (uint)((data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2]);
        return true;
    }

    public static bool TryU32BE(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
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

    public static bool TryU64LE(ReadOnlySpan<byte> data, int offset, out ulong value)
    {
        value = 0;
        if (offset < 0 || offset + 8 > data.Length)
        {
            return false;
        }

        for (var i = 7; i >= 0; i--)
        {
            value = (value << 8) | data[offset + i];
        }

        return true;
    }

    /// <summary>ID3v2 sizes are stored as four bytes of seven bits, so 0xFF never appears inside a length.</summary>
    public static bool TrySynchsafe(ReadOnlySpan<byte> data, int offset, out int value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = ((data[offset] & 0x7F) << 21) |
                ((data[offset + 1] & 0x7F) << 14) |
                ((data[offset + 2] & 0x7F) << 7) |
                (data[offset + 3] & 0x7F);
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
    /// Decodes an ID3v2 text payload using the frame's declared encoding. UTF-16 payloads may carry a byte
    /// order mark, and either half of a surrogate pair must survive, so the encoding is resolved per value
    /// rather than assumed once per file.
    /// </summary>
    public static string DecodeId3Text(ReadOnlySpan<byte> payload, byte encoding)
    {
        if (payload.IsEmpty)
        {
            return string.Empty;
        }

        return encoding switch
        {
            0 => System.Text.Encoding.Latin1.GetString(payload).TrimEnd('\0').Trim(),
            1 => DecodeUtf16WithBom(payload),
            2 => System.Text.Encoding.BigEndianUnicode.GetString(payload).TrimEnd('\0').Trim(),
            3 => System.Text.Encoding.UTF8.GetString(payload).TrimEnd('\0').Trim(),
            _ => System.Text.Encoding.Latin1.GetString(payload).TrimEnd('\0').Trim()
        };
    }

    private static string DecodeUtf16WithBom(ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE)
        {
            return System.Text.Encoding.Unicode.GetString(payload[2..]).TrimEnd('\0').Trim();
        }

        if (payload.Length >= 2 && payload[0] == 0xFE && payload[1] == 0xFF)
        {
            return System.Text.Encoding.BigEndianUnicode.GetString(payload[2..]).TrimEnd('\0').Trim();
        }

        // No marker: little-endian is what writers overwhelmingly emit.
        return System.Text.Encoding.Unicode.GetString(payload).TrimEnd('\0').Trim();
    }

    /// <summary>
    /// Splits the <c>position/total</c> form used by ID3 track and disc numbers. A missing or unparseable
    /// total is reported as null rather than zero, so "unknown" stays distinguishable from "none".
    /// </summary>
    public static (int? Number, int? Total) ParsePair(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var parts = text.Split('/', 2);
        var number = ParseInt(parts[0]);
        var total = parts.Length > 1 ? ParseInt(parts[1]) : null;
        return (number, total);
    }

    /// <summary>First integer found in a text field, tolerating decoration such as "03." or "(2011)".</summary>
    public static int? ParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var end = start;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return int.TryParse(text.AsSpan(start, end - start), out var value) ? value : null;
    }

    /// <summary>Four-digit year from a date field that may be a full timestamp.</summary>
    public static int? ParseYear(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = new string([.. System.Linq.Enumerable.TakeWhile(text.Trim(), char.IsDigit)]);
        if (digits.Length >= 4 && int.TryParse(digits[..4], out var year) && year is >= 1000 and <= 2999)
        {
            return year;
        }

        var parsed = ParseInt(text);
        return parsed is >= 1000 and <= 2999 ? parsed : null;
    }
}
