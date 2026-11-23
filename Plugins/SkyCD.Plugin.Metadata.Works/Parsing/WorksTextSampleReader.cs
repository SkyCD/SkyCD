using System;
using System.Collections.Generic;
using System.Text;

namespace SkyCD.Plugin.Metadata.Works.Parsing;

/// <summary>
/// Recovers a bounded plain-text sample from a Works document body.
/// </summary>
/// <remarks>
/// Works stores a body as a byte range inside a stream that also contains binary headers, indexes and objects.
/// The reader selects the first long printable run, which is the leading visible text, and maps only the
/// control characters both reference implementations treat as text controls. Binary markers end a run instead
/// of being guessed at, so an unsupported variant produces no sample rather than garbage.
/// </remarks>
internal static class WorksTextSampleReader
{
    private const int NarrowBodyOffset = 0x100;
    private const int MinimumSampleCharacters = 64;
    private const int MinimumSampleWords = 8;

    // Windows-1252 slots 0x80-0x9F differ from Latin-1; the five undefined slots break a run like a marker.
    private static readonly char?[] Windows1252High =
    [
        '€', null, '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', null, 'Ž', null,
        null, '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', null, 'ž', 'Ÿ'
    ];

    public static IReadOnlyList<string> Read(byte[]? body, bool isWideText, int maxCharacters)
    {
        if (body is null || body.Length == 0 || maxCharacters <= 0)
        {
            return [];
        }

        var sample = isWideText ? ReadWide(body, maxCharacters) : ReadNarrow(body, maxCharacters);
        return sample is null ? [] : ToParagraphs(sample);
    }

    private static string? ReadWide(byte[] data, int maxCharacters)
    {
        var text = new StringBuilder(Math.Min(maxCharacters, 4096));
        var characters = 0;
        var words = 0;
        var inWord = false;

        void Reset()
        {
            text.Clear();
            characters = 0;
            words = 0;
            inWord = false;
        }

        void Accept(char value, bool wordCharacter)
        {
            if (text.Length < maxCharacters)
            {
                text.Append(value);
            }

            characters++;
            if (wordCharacter)
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        string? Take()
        {
            var accepted = characters >= MinimumSampleCharacters && words >= MinimumSampleWords
                ? text.ToString()
                : null;
            Reset();
            return accepted;
        }

        var index = 0;
        while (index + 1 < data.Length)
        {
            var unit = (ushort)(data[index] | (data[index + 1] << 8));

            if (unit is >= 0xD800 and <= 0xDBFF)
            {
                if (index + 3 < data.Length)
                {
                    var low = (ushort)(data[index + 2] | (data[index + 3] << 8));
                    if (low is >= 0xDC00 and <= 0xDFFF)
                    {
                        Accept((char)unit, true);
                        Accept((char)low, true);
                        index += 4;
                        continue;
                    }
                }

                if (Take() is { } unpaired)
                {
                    return unpaired;
                }

                index += 2;
                continue;
            }

            switch (MapWide(unit, out var mapped))
            {
                case Acceptance.Accept:
                    Accept(mapped, !char.IsWhiteSpace(mapped));
                    break;
                case Acceptance.Break:
                    if (Take() is { } broken)
                    {
                        return broken;
                    }

                    break;
            }

            index += 2;
        }

        return Take();
    }

    private static string? ReadNarrow(byte[] data, int maxCharacters)
    {
        if (data.Length <= NarrowBodyOffset)
        {
            return null;
        }

        var text = new StringBuilder(Math.Min(maxCharacters, 4096));
        var characters = 0;
        var words = 0;
        var inWord = false;

        void Reset()
        {
            text.Clear();
            characters = 0;
            words = 0;
            inWord = false;
        }

        void Accept(char value)
        {
            if (text.Length < maxCharacters)
            {
                text.Append(value);
            }

            characters++;
            if (char.IsWhiteSpace(value))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                words++;
                inWord = true;
            }
        }

        string? Take()
        {
            var accepted = characters >= MinimumSampleCharacters && words >= MinimumSampleWords
                ? text.ToString()
                : null;
            Reset();
            return accepted;
        }

        for (var index = NarrowBodyOffset; index < data.Length; index++)
        {
            switch (MapNarrow(data[index], out var mapped))
            {
                case Acceptance.Accept:
                    Accept(mapped);
                    break;
                case Acceptance.Break:
                    if (Take() is { } broken)
                    {
                        return broken;
                    }

                    break;
            }
        }

        return Take();
    }

    private static Acceptance MapWide(ushort unit, out char mapped)
    {
        mapped = '\0';
        switch (unit)
        {
            case 0x09:
                mapped = ' ';
                return Acceptance.Accept;
            case 0x0A:
            case 0x0B:
            case 0x0C:
            case 0x0D:
                mapped = '\n';
                return Acceptance.Accept;
            case 0x1E:
                mapped = '\u2011';
                return Acceptance.Accept;
            case 0x1F:
                mapped = '\u00A0';
                return Acceptance.Accept;
            case 0x23:
            case 0xFFFC:
                return Acceptance.Skip;
            case 0xFFFE:
            case 0xFFFF:
                return Acceptance.Break;
        }

        if (unit < 0x20 || unit is >= 0xDC00 and <= 0xDFFF)
        {
            return Acceptance.Break;
        }

        var value = (char)unit;
        if (char.IsControl(value))
        {
            return Acceptance.Break;
        }

        switch (char.GetUnicodeCategory(value))
        {
            case System.Globalization.UnicodeCategory.Control:
            case System.Globalization.UnicodeCategory.Format:
            case System.Globalization.UnicodeCategory.Surrogate:
            case System.Globalization.UnicodeCategory.PrivateUse:
            case System.Globalization.UnicodeCategory.OtherNotAssigned:
                return Acceptance.Break;
        }

        mapped = value;
        return Acceptance.Accept;
    }

    private static Acceptance MapNarrow(byte unit, out char mapped)
    {
        mapped = '\0';
        switch (unit)
        {
            case 0x09:
                mapped = ' ';
                return Acceptance.Accept;
            case 0x0A:
            case 0x0B:
            case 0x0C:
            case 0x0D:
                mapped = '\n';
                return Acceptance.Accept;
            case 0x11:
                mapped = '\u2011';
                return Acceptance.Accept;
            case 0x12:
                mapped = ' ';
                return Acceptance.Accept;
            case 0x1F:
                return Acceptance.Skip;
        }

        if (unit is >= 0x80 and <= 0x9F)
        {
            var punctuation = Windows1252High[unit - 0x80];
            if (punctuation is null)
            {
                return Acceptance.Break;
            }

            mapped = punctuation.Value;
            return Acceptance.Accept;
        }

        if (unit is >= 0x20 and <= 0x7E || unit >= 0xA0)
        {
            // The remaining accepted bytes are identical in Windows-1252, the Works fallback code page.
            mapped = unit == 0xA0 ? ' ' : (char)unit;
            return unit == 0xAD ? Acceptance.Skip : Acceptance.Accept;
        }

        return Acceptance.Break;
    }

    private static IReadOnlyList<string> ToParagraphs(string sample)
    {
        var paragraphs = new List<string>();
        var line = new StringBuilder();

        void Flush()
        {
            var paragraph = Collapse(line);
            if (paragraph.Length > 0)
            {
                paragraphs.Add(paragraph);
            }

            line.Clear();
        }

        foreach (var value in sample)
        {
            if (value == '\n')
            {
                Flush();
            }
            else
            {
                line.Append(value);
            }
        }

        Flush();
        return paragraphs;
    }

    private static string Collapse(StringBuilder line)
    {
        var collapsed = new StringBuilder(line.Length);
        var pendingSpace = false;
        foreach (var value in line.ToString())
        {
            if (char.IsWhiteSpace(value))
            {
                if (collapsed.Length > 0)
                {
                    pendingSpace = true;
                }

                continue;
            }

            if (pendingSpace)
            {
                collapsed.Append(' ');
                pendingSpace = false;
            }

            collapsed.Append(value);
        }

        return collapsed.ToString();
    }

    private enum Acceptance
    {
        Break,
        Skip,
        Accept
    }
}
