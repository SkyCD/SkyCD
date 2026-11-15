using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Directory = MetadataExtractor.Directory;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Reads the capture date out of EXIF. The decoder owns geometry and orientation; EXIF is consulted only for
/// the one fact it is the authority on, and a source without EXIF simply produces no capture date rather
/// than a failure.
/// </summary>
internal static class ExifReader
{
    /// <summary>EXIF dates are fixed-format ASCII, independent of any host or user culture.</summary>
    private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    /// <summary>
    /// Returns the capture instant, or null when the source carries none. EXIF stores local wall-clock time,
    /// so the <c>OffsetTimeOriginal</c> tag is applied when present and UTC is assumed otherwise; the offset
    /// travels with the value instead of the wall clock being silently shifted.
    /// </summary>
    public static DateTimeOffset? TryReadCapturedAt(Stream stream)
    {
        stream.Position = 0;

        IReadOnlyList<Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(new NonClosingStream(stream));
        }
        catch (Exception exception) when (exception is ImageProcessingException or IOException
            or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var captured = CoerceDate(subIfd?.GetObject(ExifDirectoryBase.TagDateTimeOriginal))
                       ?? CoerceDate(directories.OfType<ExifIfd0Directory>().FirstOrDefault()
                           ?.GetObject(ExifDirectoryBase.TagDateTime));

        if (captured is null)
        {
            return null;
        }

        var offset = TryParseOffset(AsText(subIfd?.GetObject(ExifSubIfdDirectory.TagTimeZoneOriginal)));
        return new DateTimeOffset(DateTime.SpecifyKind(captured.Value, DateTimeKind.Unspecified), offset ?? TimeSpan.Zero);
    }

    /// <summary>
    /// EXIF stores dates and offsets as ASCII, but a reader may hand back a parsed value or its own string
    /// wrapper, so the representation is normalized instead of assumed.
    /// </summary>
    private static DateTime? CoerceDate(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime,
        DateTimeOffset offset => offset.DateTime,
        _ => ParseExifDate(AsText(value))
    };

    private static string? AsText(object? value) => value switch
    {
        null => null,
        string text => text,
        _ => value.ToString()
    };

    private static DateTime? ParseExifDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // ASCII fields are NUL padded to their declared length.
        var trimmed = text.Trim().TrimEnd('\0').Trim();
        return DateTime.TryParseExact(
            trimmed,
            ExifDateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Parses an EXIF offset such as <c>+02:00</c>, <c>-0500</c> or <c>Z</c>.</summary>
    private static TimeSpan? TryParseOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text is "Z" or "z")
        {
            return TimeSpan.Zero;
        }

        var sign = text[0] switch
        {
            '+' => 1,
            '-' => -1,
            _ => 0
        };

        var digits = (sign == 0 ? text : text[1..]).Replace(":", string.Empty, StringComparison.Ordinal);
        if (digits.Length is not (2 or 4) ||
            !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        var hours = digits.Length == 4 ? parsed / 100 : parsed;
        var minutes = digits.Length == 4 ? parsed % 100 : 0;
        if (hours > 23 || minutes > 59)
        {
            return null;
        }

        var magnitude = new TimeSpan(hours, minutes, 0);
        return sign < 0 ? magnitude.Negate() : magnitude;
    }
}
