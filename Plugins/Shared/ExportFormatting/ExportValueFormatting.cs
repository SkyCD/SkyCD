using System;
using System.Globalization;
using System.Text;

namespace SkyCD.Plugin.ExportFormatting;

/// <summary>
/// Shared value formatting for the catalog-list exporters (CSV, plain text and XML).
/// Keeps size, timestamp and generic display rendering identical across formats so a
/// size or date never renders one way in one export and differently in another.
/// </summary>
/// <remarks>
/// A <see langword="null"/> culture selects culture-invariant output: sizes print as
/// plain invariant digits and timestamps as round-trip ISO-8601. A non-null culture
/// selects readable output: sizes use a digit-grouped pattern and timestamps use the
/// culture's general date/time pattern.
/// </remarks>
internal static class ExportValueFormatting
{
    private static readonly string[] TimestampFormats =
    [
        "o", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy",
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"
    ];

    /// <summary>
    /// Formats a byte-size value. Integers render invariantly as plain digits, or
    /// grouped when a culture is supplied; floating-point values use round-trip or
    /// grouped output; text is parsed invariantly and re-rendered, falling back to the
    /// trimmed text when it is not numeric.
    /// </summary>
    /// <param name="value">The raw size value, possibly a number or numeric text.</param>
    /// <param name="culture">The output culture, or <see langword="null"/> for invariant output.</param>
    /// <returns>The formatted size, or <see langword="null"/> when the value is missing.</returns>
    internal static string? FormatSize(object? value, CultureInfo? culture)
    {
        if (value is null)
        {
            return null;
        }

        if (value is long integer)
        {
            return FormatInteger(integer, culture);
        }

        if (value is int small)
        {
            return FormatInteger(small, culture);
        }

        if (value is double number)
        {
            return FormatDouble(number, culture);
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
        {
            return FormatInteger(parsedLong, culture);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            return FormatDouble(parsedDouble, culture);
        }

        return text;
    }

    /// <summary>
    /// Formats a timestamp value. Date values render as ISO-8601 in UTC, or with the
    /// culture's general date/time pattern when a culture is supplied; text is parsed
    /// against the shared timestamp formats and re-rendered, falling back to the
    /// trimmed text when it is not a recognizable timestamp.
    /// </summary>
    /// <param name="value">The raw timestamp value, possibly a date or date text.</param>
    /// <param name="culture">The output culture, or <see langword="null"/> for invariant output.</param>
    /// <returns>The formatted timestamp, or <see langword="null"/> when the value is missing.</returns>
    internal static string? FormatTimestamp(object? value, CultureInfo? culture) => value switch
    {
        null => null,
        DateTimeOffset offset => FormatParsed(offset, culture),
        DateTime dateTime => FormatDateTime(dateTime, culture),
        _ => FormatTimestampText(Convert.ToString(value, CultureInfo.InvariantCulture), culture)
    };

    /// <summary>
    /// Parses timestamp text against the formats shared by every list exporter.
    /// </summary>
    /// <param name="text">The candidate timestamp text.</param>
    /// <param name="parsed">The parsed UTC-offset timestamp when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the text matched one of the shared formats.</returns>
    internal static bool TryParseTimestamp(string text, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(text, TimestampFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);

    /// <summary>
    /// Renders a value for display. Text passes through, booleans use their invariant
    /// names, and everything else formats invariantly.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The display text, or <see langword="null"/> when the value is null.</returns>
    internal static string? ToDisplayText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? bool.TrueString : bool.FalseString,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Replaces carriage returns, line feeds and tabs with single spaces, keeping every
    /// other character so single-line layouts such as plain text cannot be broken apart.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    internal static string NormalizeControls(string text)
    {
        if (text.IndexOfAny(['\r', '\n', '\t']) < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character is '\r' or '\n' or '\t' ? ' ' : character);
        }

        return builder.ToString();
    }

    private static string? FormatTimestampText(string? text, CultureInfo? culture)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return TryParseTimestamp(trimmed, out var parsed)
            ? FormatParsed(parsed, culture)
            : trimmed;
    }

    private static string FormatDateTime(DateTime dateTime, CultureInfo? culture)
    {
        var zoned = dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime;
        return culture is null
            ? new DateTimeOffset(zoned).ToString("o", CultureInfo.InvariantCulture)
            : zoned.ToString(culture);
    }

    private static string FormatParsed(DateTimeOffset value, CultureInfo? culture) =>
        culture is null
            ? value.ToString("o", CultureInfo.InvariantCulture)
            : value.ToString(culture);

    private static string FormatInteger(long value, CultureInfo? culture) =>
        culture is null
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString("N0", culture);

    private static string FormatDouble(double value, CultureInfo? culture) =>
        culture is null
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : value.ToString("N", culture);
}
