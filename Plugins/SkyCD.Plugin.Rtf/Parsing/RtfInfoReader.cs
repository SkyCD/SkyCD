using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkyCD.Plugin.Rtf.Models;

namespace SkyCD.Plugin.Rtf.Parsing;

/// <summary>
/// Reads the RTF header and information group without interpreting document formatting.
/// </summary>
/// <remarks>
/// The parser is intentionally narrow: it tracks group nesting, destination names and the escaped text of
/// the information group. Unknown destinations are skipped, which keeps a malformed or vendor-specific
/// group from discarding the fields around it. Timestamps in RTF carry no timezone, so they are published
/// with a UTC offset only because the schema requires one; the original wall-clock values are preserved.
/// </remarks>
internal static class RtfInfoReader
{
    private const int DefaultAnsiCodePage = 1252;

    private static readonly Dictionary<string, string> Destinations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title",
        ["subject"] = "subject",
        ["author"] = "author",
        ["manager"] = "manager",
        ["company"] = "company",
        ["operator"] = "operator",
        ["category"] = "category",
        ["keywords"] = "keywords",
        ["doccomm"] = "comment",
        ["comment"] = "comment"
    };

    public static RtfInfo Read(string rtf)
    {
        ArgumentNullException.ThrowIfNull(rtf);

        var header = ScanHeader(rtf);
        var parsed = header.InfoGroup is null
            ? ParsedInfo.Empty
            : ParseInfo(header.InfoGroup, header.DecodeCodePage);

        return new RtfInfo(
            header.RtfVersion,
            header.DeclaredAnsiCodePage,
            header.IsProtected,
            parsed.Text("title"),
            parsed.Text("subject"),
            parsed.Text("author"),
            parsed.Text("manager"),
            parsed.Text("company"),
            parsed.Text("operator"),
            parsed.Text("category"),
            parsed.Text("keywords"),
            parsed.Text("comment"),
            parsed.Number("version"),
            parsed.Number("edmins"),
            parsed.Number("nofpages"),
            parsed.Number("nofwords"),
            parsed.Number("nofchars"),
            parsed.Number("nofcharsws"),
            parsed.Timestamp("creatim"),
            parsed.Timestamp("revtim"),
            parsed.Timestamp("printim"));
    }

    private static HeaderScan ScanHeader(string rtf)
    {
        int? rtfVersion = null;
        int? declaredAnsiCodePage = null;
        var decodeCodePage = DefaultAnsiCodePage;
        var isProtected = false;
        var groupStarts = new Stack<int>();
        var infoStart = -1;
        var infoEnd = -1;
        var index = 0;

        while (index < rtf.Length)
        {
            var ch = rtf[index];
            if (ch == '{')
            {
                groupStarts.Push(index);
                index++;
            }
            else if (ch == '}')
            {
                if (groupStarts.Count > 0)
                {
                    var start = groupStarts.Pop();
                    if (start == infoStart && infoEnd < 0)
                    {
                        infoEnd = index;
                    }
                }

                index++;
            }
            else if (ch == '\\')
            {
                if (index + 1 < rtf.Length && rtf[index + 1] == '\'')
                {
                    index = Math.Min(rtf.Length, index + 4);
                    continue;
                }

                if (TryReadControlWord(rtf, ref index, out var word, out var parameter))
                {
                    switch (word)
                    {
                        case "rtf":
                            rtfVersion ??= parameter;
                            break;
                        case "ansicpg":
                            if (parameter.HasValue)
                            {
                                declaredAnsiCodePage = parameter;
                                decodeCodePage = parameter.Value;
                            }

                            break;
                        case "info":
                            if (infoStart < 0 && groupStarts.Count > 0)
                            {
                                infoStart = groupStarts.Peek();
                            }

                            break;
                        case "passwordhash":
                        case "formprot":
                        case "annotprot":
                        case "readprot":
                        case "revprot":
                            isProtected = true;
                            break;
                    }
                }
                else
                {
                    index++;
                }
            }
            else
            {
                index++;
            }
        }

        string? infoGroup = null;
        if (infoStart >= 0)
        {
            infoGroup = rtf[infoStart..(infoEnd >= 0 ? infoEnd + 1 : rtf.Length)];
        }

        return new HeaderScan(infoGroup, rtfVersion, declaredAnsiCodePage, decodeCodePage, isProtected);
    }

    private static ParsedInfo ParseInfo(string infoRtf, int codePage)
    {
        var parsed = new ParsedInfo();
        var stack = new Stack<InfoState>();
        var state = new InfoState();
        var index = 0;

        while (index < infoRtf.Length)
        {
            var ch = infoRtf[index];
            if (ch == '{')
            {
                stack.Push(state.Clone());
                index++;
            }
            else if (ch == '}')
            {
                Commit(state, parsed);
                state = stack.Count > 0 ? stack.Pop() : new InfoState();
                index++;
            }
            else if (ch == '\\')
            {
                index++;
                if (index >= infoRtf.Length)
                {
                    break;
                }

                var next = infoRtf[index];
                switch (next)
                {
                    case '\\':
                    case '{':
                    case '}':
                        Append(state, next);
                        index++;
                        break;
                    case '~':
                        Append(state, ' ');
                        index++;
                        break;
                    case '_':
                    case '-':
                        Append(state, '-');
                        index++;
                        break;
                    case '*':
                        state.Skip = true;
                        index++;
                        break;
                    case '\'':
                        var escapeStart = index;
                        Append(state, DecodeAnsi(ReadAnsiBytes(infoRtf, ref index), codePage));
                        if (index == escapeStart)
                        {
                            index++;
                        }

                        break;
                    case '\r':
                    case '\n':
                        index++;
                        break;
                    default:
                        index--;
                        if (TryReadControlWord(infoRtf, ref index, out var word, out var parameter))
                        {
                            var fallbackCount = HandleWord(word, parameter, state, parsed);
                            if (fallbackCount > 0)
                            {
                                SkipUnicodeFallback(infoRtf, ref index, fallbackCount);
                            }
                        }
                        else
                        {
                            index++;
                        }

                        break;
                }
            }
            else if (ch is '\r' or '\n')
            {
                index++;
            }
            else
            {
                Append(state, ch);
                index++;
            }
        }

        Commit(state, parsed);
        return parsed;
    }

    private static int HandleWord(string word, int? parameter, InfoState state, ParsedInfo parsed)
    {
        if (Destinations.TryGetValue(word, out var destination))
        {
            state.Destination = destination;
            state.Text = new StringBuilder();
            state.OwnsDestination = true;
            return 0;
        }

        switch (word)
        {
            case "creatim":
            case "revtim":
            case "printim":
                state.Timestamp = new TimestampBuilder();
                state.TimestampName = word;
                state.OwnsTimestamp = true;
                state.Destination = null;
                state.Text = null;
                state.OwnsDestination = false;
                return 0;
            case "yr":
            case "mo":
            case "dy":
            case "hr":
            case "min":
            case "sec":
                state.Timestamp?.Set(word, parameter);
                return 0;
            case "uc":
                if (parameter.HasValue)
                {
                    state.UnicodeSkip = Math.Max(0, parameter.Value);
                }

                return 0;
            case "u":
                if (parameter.HasValue)
                {
                    var value = parameter.Value < 0 ? parameter.Value + 65536 : parameter.Value;
                    Append(state, (char)value);
                    return state.UnicodeSkip;
                }

                return 0;
            case "version":
            case "edmins":
            case "nofpages":
            case "nofwords":
            case "nofchars":
            case "nofcharsws":
                parsed.SetNumber(word, parameter);
                return 0;
            case "par":
            case "line":
                Append(state, '\n');
                return 0;
            case "tab":
                Append(state, '\t');
                return 0;
            case "lquote":
                Append(state, '\u2018');
                return 0;
            case "rquote":
                Append(state, '\u2019');
                return 0;
            case "ldblquote":
                Append(state, '\u201C');
                return 0;
            case "rdblquote":
                Append(state, '\u201D');
                return 0;
            case "bullet":
                Append(state, '\u2022');
                return 0;
            case "endash":
                Append(state, '\u2013');
                return 0;
            case "emdash":
                Append(state, '\u2014');
                return 0;
            case "emspace":
                Append(state, '\u2003');
                return 0;
            case "enspace":
                Append(state, '\u2002');
                return 0;
            case "qmspace":
                Append(state, '\u2005');
                return 0;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Skips the ANSI fallback that follows a <c>\uN</c> value, so it is not appended a second time.
    /// </summary>
    private static void SkipUnicodeFallback(string text, ref int index, int count)
    {
        var remaining = count;
        while (remaining > 0 && index < text.Length)
        {
            if (index + 1 < text.Length && text[index] == '\\' && text[index + 1] == '\'')
            {
                index = Math.Min(text.Length, index + 4);
                remaining--;
            }
            else if (text[index] is not '{' and not '}' and not '\\')
            {
                index++;
                remaining--;
            }
            else
            {
                break;
            }
        }
    }

    private static void Commit(InfoState state, ParsedInfo parsed)
    {
        if (state.OwnsDestination && !state.Skip && state.Destination is { } key && state.Text is { } text)
        {
            var value = text.ToString().Trim();
            if (value.Length > 0)
            {
                parsed.AppendText(key, value);
            }
        }

        if (state.OwnsTimestamp && !state.Skip && state.TimestampName is { } timestampKey &&
            state.Timestamp is { } timestamp && timestamp.HasAny)
        {
            parsed.SetTimestamp(timestampKey, timestamp.Build());
        }
    }

    private static void Append(InfoState state, char ch)
    {
        if (!state.Skip && state.Text is not null)
        {
            state.Text.Append(ch);
        }
    }

    private static void Append(InfoState state, string text)
    {
        if (!state.Skip && state.Text is not null)
        {
            state.Text.Append(text);
        }
    }

    private static List<byte> ReadAnsiBytes(string text, ref int index)
    {
        var bytes = new List<byte>();
        while (index + 2 < text.Length && text[index] == '\'')
        {
            if (!byte.TryParse(text.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out var value))
            {
                break;
            }

            bytes.Add(value);
            index += 3;

            if (index + 1 < text.Length && text[index] == '\\' && text[index + 1] == '\'')
            {
                index++;
                continue;
            }

            break;
        }

        return bytes;
    }

    private static string DecodeAnsi(List<byte> bytes, int codePage)
    {
        if (bytes.Count == 0)
        {
            return string.Empty;
        }

        var data = bytes.ToArray();
        if (codePage == 1252 || codePage <= 0)
        {
            return DecodeWindows1252(data);
        }

        try
        {
            return Encoding.GetEncoding(codePage).GetString(data);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return DecodeWindows1252(data);
        }
    }

    private static string DecodeWindows1252(byte[] data)
    {
        var chars = new char[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            chars[i] = data[i] is >= 0x80 and <= 0x9F
                ? Windows1252[data[i] - 0x80]
                : (char)data[i];
        }

        return new string(chars);
    }

    /// <summary>Windows-1252 mappings for 0x80-0x9F, the range that differs from ISO-8859-1.</summary>
    private const string Windows1252 =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F" +
        "\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";

    private static bool TryReadControlWord(string text, ref int index, out string word, out int? parameter)
    {
        word = string.Empty;
        parameter = null;

        var i = index + 1;
        if (i >= text.Length || !char.IsLetter(text[i]))
        {
            index = i;
            return false;
        }

        var start = i;
        while (i < text.Length && char.IsLetter(text[i]))
        {
            i++;
        }

        word = text[start..i].ToLowerInvariant();

        var sign = 1;
        if (i < text.Length && text[i] == '-')
        {
            sign = -1;
            i++;
        }

        var numberStart = i;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if (i > numberStart &&
            int.TryParse(text.AsSpan(numberStart, i - numberStart), NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed))
        {
            parameter = parsed * sign;
        }

        if (i < text.Length && text[i] == ' ')
        {
            i++;
        }

        index = i;
        return true;
    }

    private sealed class InfoState
    {
        public string? Destination { get; set; }
        public StringBuilder? Text { get; set; }
        public bool OwnsDestination { get; set; }
        public TimestampBuilder? Timestamp { get; set; }
        public string? TimestampName { get; set; }
        public bool OwnsTimestamp { get; set; }
        public bool Skip { get; set; }
        public int UnicodeSkip { get; set; } = 1;

        public InfoState Clone() => new()
        {
            Destination = Destination,
            Text = Text,
            OwnsDestination = false,
            Timestamp = Timestamp,
            TimestampName = TimestampName,
            OwnsTimestamp = false,
            Skip = Skip,
            UnicodeSkip = UnicodeSkip
        };
    }

    private sealed class TimestampBuilder
    {
        private int? year;
        private int? month;
        private int? day;
        private int? hour;
        private int? minute;
        private int? second;

        public bool HasAny => year.HasValue || month.HasValue || day.HasValue || hour.HasValue ||
                              minute.HasValue || second.HasValue;

        public void Set(string word, int? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            switch (word)
            {
                case "yr":
                    year = value;
                    break;
                case "mo":
                    month = value;
                    break;
                case "dy":
                    day = value;
                    break;
                case "hr":
                    hour = value;
                    break;
                case "min":
                    minute = value;
                    break;
                case "sec":
                    second = value;
                    break;
            }
        }

        public DateTimeOffset? Build()
        {
            if (!year.HasValue || !month.HasValue || !day.HasValue)
            {
                return null;
            }

            try
            {
                return new DateTimeOffset(year.Value, month.Value, day.Value, hour ?? 0, minute ?? 0,
                    second ?? 0, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }

    private sealed class ParsedInfo
    {
        public static ParsedInfo Empty { get; } = new();

        private readonly Dictionary<string, StringBuilder> texts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> timestamps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> numbers = new(StringComparer.OrdinalIgnoreCase);

        public void AppendText(string key, string value)
        {
            if (texts.TryGetValue(key, out var existing))
            {
                if (existing.Length > 0)
                {
                    existing.Append('\n');
                }

                existing.Append(value);
            }
            else
            {
                texts[key] = new StringBuilder(value);
            }
        }

        public void SetTimestamp(string key, DateTimeOffset? value)
        {
            if (value.HasValue)
            {
                timestamps[key] = value.Value;
            }
        }

        public void SetNumber(string key, int? value)
        {
            if (value.HasValue)
            {
                numbers[key] = value.Value;
            }
        }

        public string? Text(string key) =>
            texts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.ToString())
                ? value.ToString().Trim()
                : null;

        public int? Number(string key) => numbers.TryGetValue(key, out var value) ? value : null;

        public DateTimeOffset? Timestamp(string key) =>
            timestamps.TryGetValue(key, out var value) ? value : null;
    }

    private sealed record HeaderScan(
        string? InfoGroup,
        int? RtfVersion,
        int? DeclaredAnsiCodePage,
        int DecodeCodePage,
        bool IsProtected);
}
