using System;
using System.Text.RegularExpressions;
using Humanizer;

namespace SkyCD.Formatting;

public static class SizeFormatting
{
    private static readonly Regex NumberUnitWithoutSpace = new(
        "(?<number>\\d(?:[\\d.,]*\\d)?)(?<unit>[A-Za-z]+)$",
        RegexOptions.Compiled);

    public static string FormatBytes(long bytes, string format = "0.##", bool removeSpace = false)
    {
        var text = bytes.Bytes().Humanize(format);
        return removeSpace
            ? text.Replace(" ", string.Empty, StringComparison.Ordinal)
            : text;
    }

    public static string FormatAboutDialogBytes(long bytes)
    {
        return Math.Abs(bytes) < 1024
            ? bytes.Bytes().ToString("0")
            : bytes.Bytes().ToString("0.0");
    }

    public static bool TryParseBytes(string raw, out long bytes)
    {
        bytes = 0;
        var value = raw.Trim();

        if (ByteSize.TryParse(value, out var size))
        {
            bytes = (long)size.Bytes;
            return true;
        }

        var normalized = NumberUnitWithoutSpace.Replace(value, "${number} ${unit}");
        if (ByteSize.TryParse(normalized, out size))
        {
            bytes = (long)size.Bytes;
            return true;
        }

        if (long.TryParse(value, out bytes))
        {
            return true;
        }

        return false;
    }
}
