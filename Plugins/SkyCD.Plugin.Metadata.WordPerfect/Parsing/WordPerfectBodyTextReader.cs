using System;
using System.Text;

namespace SkyCD.Plugin.Metadata.WordPerfect.Parsing;

/// <summary>
/// Recovers a bounded leading text sample from a WP6 body stream.
/// </summary>
/// <remarks>
/// The WP6 body is a byte stream of characters and function groups. Groups are skipped by their declared
/// size and must be closed by a repeated group id, so a corrupt or truncated length ends the walk instead of
/// shifting the reader into unrelated bytes. Nothing outside the bytes already in hand is followed, and the
/// collected text never grows past the requested character count.
/// </remarks>
internal static class WordPerfectBodyTextReader
{
    /// <summary>Id, subgroup, u16 size, flags, u16 non-deletable size, and the three byte trailer.</summary>
    private const int VariableGroupMinimumBytes = 10;
    private const byte LastSingleByteGroup = 0xCF;
    private const byte LastVariableGroup = 0xEF;
    private const byte FirstFixedGroup = 0xF0;
    private const byte LastFixedGroup = 0xFE;

    /// <summary>WordPerfect's single-byte multinational set: bytes 0x01-0x20, in order.</summary>
    private static readonly char[] Multinational =
    [
        'å', 'Å', 'æ', 'Æ', 'ä', 'Ä', 'á', 'à', 'â', 'ã', 'Ã', 'ç', 'Ç', 'ë', 'é', 'É',
        'è', 'ê', 'í', 'ñ', 'Ñ', 'ø', 'Ø', 'õ', 'Õ', 'ö', 'Ö', 'ü', 'Ü', 'ú', 'ù', 'ß'
    ];

    /// <summary>Total byte size of the fixed-length groups 0xF0-0xFE, indexed by <c>id - 0xF0</c>.</summary>
    private static readonly byte[] FixedGroupBytes = [4, 5, 3, 3, 3, 3, 4, 4, 4, 5, 5, 6, 6, 8, 8];

    /// <summary>
    /// Returns the leading body text, stopping at <paramref name="maxCharacters"/> collected characters, at
    /// the end of the stream, or at the first group whose framing does not close. Never throws for malformed
    /// content: the text recovered so far is the result.
    /// </summary>
    public static string Read(ReadOnlySpan<byte> data, int start, int maxCharacters)
    {
        if (maxCharacters <= 0 || start < 0 || start >= data.Length)
        {
            return string.Empty;
        }

        var text = new StringBuilder(Math.Min(maxCharacters, 256));
        var position = start;
        while (position < data.Length && text.Length < maxCharacters)
        {
            var value = data[position];

            if (value == 0x00)
            {
                // WordPerfect writes a null where a deleted token was; it carries no content.
                position++;
                continue;
            }

            if (value <= 0x1F)
            {
                Append(text, Multinational[value - 1], maxCharacters);
                position++;
                continue;
            }

            if (value <= 0x7F)
            {
                Append(text, (char)value, maxCharacters);
                position++;
                continue;
            }

            if (value <= LastSingleByteGroup)
            {
                if (SingleByteText(value) is { } character)
                {
                    Append(text, character, maxCharacters);
                }

                position++;
                continue;
            }

            if (value <= LastVariableGroup)
            {
                if (!TrySkipVariableGroup(data, position, out var next))
                {
                    break;
                }

                position = next;
                continue;
            }

            if (value <= LastFixedGroup)
            {
                if (!TrySkipFixedGroup(data, position, out var next))
                {
                    break;
                }

                position = next;
                continue;
            }

            // 0xFF has no assigned size, so nothing can be skipped safely; the token itself is consumed.
            position++;
        }

        return text.ToString();
    }

    /// <summary>
    /// Text contributed by the single-byte function tokens 0x80-0xCF. Everything else in that range is
    /// formatting intent with no printable equivalent.
    /// </summary>
    private static char? SingleByteText(byte value) => value switch
    {
        0x80 or 0x81 => ' ',   // soft and hard space
        0x84 or 0x85 => '-',   // hard and auto hyphen
        0x87 => '\n',          // dormant hard return
        >= 0xB4 and <= 0xCF => '\n', // paragraph, line, page, column and table breaks
        _ => null
    };

    /// <summary>
    /// Variable-length group layout: <c>id | subgroup | u16 size | flags | ... | u16 size | id</c>, where the
    /// size covers the whole group starting at the id. Both the repeated size and the closing id must match.
    /// </summary>
    private static bool TrySkipVariableGroup(ReadOnlySpan<byte> data, int position, out int next)
    {
        next = position;
        if (position + VariableGroupMinimumBytes > data.Length)
        {
            return false;
        }

        var size = data[position + 2] | (data[position + 3] << 8);
        if (size < VariableGroupMinimumBytes || position + size > data.Length)
        {
            return false;
        }

        var repeatedSize = data[position + size - 3] | (data[position + size - 2] << 8);
        if (repeatedSize != size || data[position + size - 1] != data[position])
        {
            return false;
        }

        next = position + size;
        return true;
    }

    /// <summary>Fixed-length groups carry no size field; the id selects a constant and the last byte repeats it.</summary>
    private static bool TrySkipFixedGroup(ReadOnlySpan<byte> data, int position, out int next)
    {
        next = position;
        var size = FixedGroupBytes[data[position] - FirstFixedGroup];
        if (position + size > data.Length || data[position + size - 1] != data[position])
        {
            return false;
        }

        next = position + size;
        return true;
    }

    private static void Append(StringBuilder text, char character, int maxCharacters)
    {
        if (text.Length >= maxCharacters)
        {
            return;
        }

        if (character == '\n' && text.Length > 0 && text[^1] == '\n')
        {
            return;
        }

        text.Append(character);
    }
}
