using System;
using System.Collections.Generic;
using System.Text;

namespace SkyCD.Plugin.WordPerfect.Models;

/// <summary>One parsed extended-document-summary group. Dates and text are mutually exclusive.</summary>
internal readonly record struct WordPerfectSummaryValue(string? Text, DateTimeOffset? Timestamp);

/// <summary>
/// Parses the extended document summary packet of a WordPerfect 6+ document.
/// </summary>
/// <remarks>
/// The packet is a sequence of length-prefixed groups. Each group carries a two-byte tag, two bytes of type
/// flags, a null-terminated name and either a null-terminated text value or a ten-byte date. A group length of
/// zero ends the packet. Strings are word strings: the low byte is the character and the high byte selects the
/// WordPerfect character set. Set 0 and the unlisted sets are decoded from the low byte, which is exact for
/// ASCII and best-effort elsewhere; set 1 (the multinational set) is decoded as Windows-1252, which covers
/// accented Latin names. The first valid group for a tag wins, matching WordPerfect's own writer. A truncated
/// or damaged packet yields the groups parsed so far rather than failing the whole extraction.
/// </remarks>
internal static class WordPerfectSummary
{
    private const int MaxGroups = 4096;

    public static IReadOnlyDictionary<ushort, WordPerfectSummaryValue> Parse(ReadOnlySpan<byte> data)
    {
        var values = new Dictionary<ushort, WordPerfectSummaryValue>();
        var offset = 0;
        var groups = 0;
        while (offset + 6 <= data.Length && groups++ < MaxGroups)
        {
            var groupLength = ReadU16(data, offset);
            if (groupLength < 6 || (long)offset + groupLength > data.Length)
            {
                break;
            }

            var end = offset + groupLength;
            var tag = ReadU16(data, offset + 2);
            var (_, afterName) = ReadWordString(data, offset + 6, end);

            if (IsDateTag(tag))
            {
                var date = ReadDate(data, afterName, end);
                if (date is { } value && !values.ContainsKey(tag))
                {
                    values.Add(tag, new WordPerfectSummaryValue(null, value));
                }
            }
            else
            {
                var (text, _) = ReadWordString(data, afterName, end);
                if (text.Length > 0 && !values.ContainsKey(tag))
                {
                    values.Add(tag, new WordPerfectSummaryValue(text, null));
                }
            }

            offset = end;
        }

        return values;
    }

    private static bool IsDateTag(ushort tag) => tag is 14 or 15 or 37 or 39 or 49;

    private static (string Text, int NextOffset) ReadWordString(ReadOnlySpan<byte> data, int offset, int end)
    {
        var builder = new StringBuilder();
        var position = offset;
        while (position + 2 <= end)
        {
            var word = ReadU16(data, position);
            position += 2;
            if (word == 0)
            {
                break;
            }

            var character = DecodeCharacter((byte)word, (byte)(word >> 8));
            if (character is '\r' or '\n' or '\t')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }
            else if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return (builder.ToString().Trim(), position);
    }

    private static char DecodeCharacter(byte value, byte characterSet) =>
        characterSet == 1 ? DecodeMultinational(value) : (char)value;

    private static char DecodeMultinational(byte value) => value switch
    {
        0x80 => '\u20AC',
        0x82 => '\u201A',
        0x83 => '\u0192',
        0x84 => '\u201E',
        0x85 => '\u2026',
        0x86 => '\u2020',
        0x87 => '\u2021',
        0x88 => '\u02C6',
        0x89 => '\u2030',
        0x8A => '\u0160',
        0x8B => '\u2039',
        0x8C => '\u0152',
        0x8E => '\u017D',
        0x91 => '\u2018',
        0x92 => '\u2019',
        0x93 => '\u201C',
        0x94 => '\u201D',
        0x95 => '\u2022',
        0x96 => '\u2013',
        0x97 => '\u2014',
        0x98 => '\u02DC',
        0x99 => '\u2122',
        0x9A => '\u0161',
        0x9B => '\u203A',
        0x9C => '\u0153',
        0x9E => '\u017E',
        0x9F => '\u0178',
        0x81 or 0x8D or 0x8F or 0x90 or 0x9D => '\uFFFD',
        _ => (char)value
    };

    private static DateTimeOffset? ReadDate(ReadOnlySpan<byte> data, int offset, int end)
    {
        if (offset + 10 > end)
        {
            return null;
        }

        var year = ReadU16(data, offset);
        var month = data[offset + 2];
        var day = data[offset + 3];
        var hour = data[offset + 4];
        var minute = data[offset + 5];
        var second = data[offset + 6];

        if (year is < 1900 or > 9999 || month is < 1 or > 12 || day < 1
            || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }

        // The stored date has no trustworthy UTC offset, so it is published as a nominal local time with a
        // zero offset rather than being shifted by the machine's current time zone.
        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));
}
