using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Humanizer;

namespace SkyCD.Formatting;

public static class SizeFormatting
{
    private static readonly Regex NumberUnitWithoutSpace = new(
        "(?<number>\\d(?:[\\d.,]*\\d)?)(?<unit>[A-Za-z]+)$",
        RegexOptions.Compiled);

    public static string FormatBytes(long bytes, string format = "0.##", bool removeSpace = false) =>
        FormatBytes(bytes, format, CultureInfo.CurrentCulture, removeSpace);

    public static string FormatBytes(long bytes, string format, IFormatProvider provider, bool removeSpace = false)
    {
        var text = bytes.Bytes().Humanize(format, provider);
        return removeSpace
            ? text.Replace(" ", string.Empty, StringComparison.Ordinal)
            : text;
    }

    public static string FormatBytes(ulong bytes, string format = "0.##", bool removeSpace = false) =>
        FormatBytes(bytes, format, CultureInfo.CurrentCulture, removeSpace);

    public static string FormatBytes(ulong bytes, string format, IFormatProvider provider, bool removeSpace = false)
    {
        if (bytes <= long.MaxValue)
        {
            return FormatBytes((long)bytes, format, provider, removeSpace);
        }

        var text = ((double)bytes).Bytes().Humanize(format, provider);
        return removeSpace
            ? text.Replace(" ", string.Empty, StringComparison.Ordinal)
            : text;
    }

    /// <summary>
    /// Culture-stable rendering of the legacy GenDesc byte shape: whole bytes as "N B" (including the
    /// uppercase "0 B" Humanizer no longer emits for zero) and larger values with one invariant decimal,
    /// so templates and .nfo output do not change with the user's OS culture.
    /// </summary>
    public static string FormatInvariantBytes(long bytes) =>
        bytes == 0 ? "0 B" : FormatBytes(bytes, "0.#", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="FormatInvariantBytes(long)"/>
    public static string FormatInvariantBytes(ulong bytes) =>
        bytes == 0 ? "0 B" : FormatBytes(bytes, "0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// <see cref="FormatInvariantBytes(long)"/> for template values, which arrive as doubles.
    /// The caller keeps the NaN/negative contract; finite values beyond <see cref="long.MaxValue"/>
    /// saturate instead of overflowing.
    /// </summary>
    public static string FormatInvariantBytes(double bytes) =>
        bytes >= long.MaxValue
            ? FormatInvariantBytes(ulong.MaxValue)
            : FormatInvariantBytes((long)bytes);

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