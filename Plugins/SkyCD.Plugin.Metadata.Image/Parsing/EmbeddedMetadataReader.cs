using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Xmp;
using SkyCD.Plugin.MediaMetadata.Parsing;
using MetadataDirectory = MetadataExtractor.Directory;
using MetadataPair = MetadataExtractor.KeyValuePair;

namespace SkyCD.Plugin.Metadata.Image.Parsing;

/// <summary>The catalog tags and capture date one container carries.</summary>
internal readonly record struct EmbeddedMetadata(MetadataScan Tags, DateTimeOffset? CapturedAt)
{
    public static EmbeddedMetadata Empty { get; } = new(new MetadataScan(), null);
}

/// <summary>
/// Reads the portable tag carriers of JPEG, PNG, WebP, GIF and TIFF containers through MetadataExtractor
/// and maps its directories onto the catalog's own tag, comment, rating and capture-date values.
/// </summary>
/// <remarks>
/// Geometry is never taken from here; Skia already reported it. Readers are applied from the most explicit
/// carrier to the least — XMP, then IPTC-IIM, then the EXIF directory, then PNG text chunks, then a JPEG
/// comment — so a caption or rating written by a photo editor wins over text left behind by an exporter,
/// and keywords from every carrier are merged. The container is recognized by magic bytes, because a
/// re-encoded image may keep its old extension. A damaged container yields no values instead of an
/// exception, and the library only ever reads the bounded buffer the plugin already holds.
/// </remarks>
internal static class EmbeddedMetadataReader
{
    /// <summary>EXIF stores the UTC offset companion next to the original timestamp under this id.</summary>
    private const int OffsetTimeOriginalTag = 0x9011;

    private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    public static EmbeddedMetadata Read(byte[] data)
    {
        IReadOnlyList<MetadataDirectory> directories;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            directories = ImageMetadataReader.ReadMetadata(stream);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A container the library cannot fully parse contributes no values, as an unreadable directory
            // did before; the geometry Skia already extracted stands.
            return EmbeddedMetadata.Empty;
        }

        var tags = new MetadataScan();
        ApplyXmp(directories, tags);
        ApplyIptc(directories, tags);
        ApplyExif(directories, tags);
        ApplyPngText(directories, tags);
        ApplyJpegComments(directories, tags);

        return new EmbeddedMetadata(tags, ReadCapturedAt(directories));
    }

    private static void ApplyXmp(IReadOnlyList<MetadataDirectory> directories, MetadataScan scan)
    {
        foreach (var directory in directories)
        {
            if (directory is not XmpDirectory xmp || !XmpMetadataReader.TryRead(xmp.XmpMeta, out var metadata))
            {
                continue;
            }

            foreach (var keyword in metadata.Keywords)
            {
                scan.AddKeyword(keyword);
            }

            if (metadata.Comment is not null)
            {
                scan.SetComment(metadata.Comment);
            }

            scan.SetRating(metadata.Rating);
        }
    }

    private static void ApplyIptc(IReadOnlyList<MetadataDirectory> directories, MetadataScan scan)
    {
        foreach (var directory in directories)
        {
            if (directory is not IptcDirectory iptc)
            {
                continue;
            }

            foreach (var keyword in iptc.GetStringArray(IptcDirectory.TagKeywords) ?? [])
            {
                scan.AddKeyword(keyword);
            }

            scan.SetComment(iptc.GetString(IptcDirectory.TagCaption));
        }
    }

    private static void ApplyExif(IReadOnlyList<MetadataDirectory> directories, MetadataScan scan)
    {
        foreach (var directory in directories)
        {
            switch (directory)
            {
                case ExifIfd0Directory ifd0:
                    scan.AddKeywords(ifd0.GetDescription(ExifDirectoryBase.TagWinKeywords));
                    scan.SetComment(ifd0.GetDescription(ExifDirectoryBase.TagWinComment));
                    scan.SetComment(ifd0.GetString(ExifDirectoryBase.TagImageDescription));
                    scan.SetComment(ifd0.GetDescription(ExifDirectoryBase.TagUserComment));
                    scan.SetRating(ReadRating(ifd0));
                    break;
                case ExifSubIfdDirectory subIfd:
                    scan.SetComment(subIfd.GetDescription(ExifDirectoryBase.TagUserComment));
                    break;
            }
        }
    }

    /// <summary>
    /// PNG stores keywords and captions as uncompressed or compressed text chunks. The library inflates a
    /// compressed chunk before this mapping sees it, so both reach the catalog with the same meaning.
    /// </summary>
    private static void ApplyPngText(IReadOnlyList<MetadataDirectory> directories, MetadataScan scan)
    {
        foreach (var directory in directories)
        {
            if (directory is not PngDirectory png ||
                png.GetObject(PngDirectory.TagTextualData) is not IEnumerable<MetadataPair> pairs)
            {
                continue;
            }

            foreach (var pair in pairs)
            {
                ApplyPngTextValue(pair.Key, pair.Value.ToString(), scan);
            }
        }
    }

    private static void ApplyPngTextValue(string keyword, string text, MetadataScan scan)
    {
        if (keyword.Equals("Keywords", StringComparison.OrdinalIgnoreCase))
        {
            scan.AddKeywords(text);
        }
        else if (keyword.Equals("Comment", StringComparison.OrdinalIgnoreCase) ||
                 keyword.Equals("Description", StringComparison.OrdinalIgnoreCase))
        {
            scan.SetComment(text);
        }
    }

    private static void ApplyJpegComments(IReadOnlyList<MetadataDirectory> directories, MetadataScan scan)
    {
        foreach (var directory in directories)
        {
            if (directory is JpegCommentDirectory comment)
            {
                scan.SetComment(comment.GetString(JpegCommentDirectory.TagComment));
            }
        }
    }

    private static int? ReadRating(ExifIfd0Directory ifd0) =>
        ifd0.TryGetInt32(ExifDirectoryBase.TagRating, out var rating) ? rating
        : ifd0.TryGetInt32(ExifDirectoryBase.TagRatingPercent, out var percent) ? percent
        : null;

    /// <summary>
    /// Prefers <c>DateTimeOriginal</c> with its offset companion over the IFD0 <c>DateTime</c> fallback. EXIF
    /// stores local wall-clock time, so the offset is applied when present and UTC is assumed otherwise; the
    /// offset travels with the value instead of the wall clock being silently shifted.
    /// </summary>
    private static DateTimeOffset? ReadCapturedAt(IReadOnlyList<MetadataDirectory> directories)
    {
        string? original = null;
        string? offsetText = null;
        string? fallback = null;

        foreach (var directory in directories)
        {
            switch (directory)
            {
                case ExifSubIfdDirectory subIfd:
                    original ??= subIfd.GetString(ExifDirectoryBase.TagDateTimeOriginal);
                    offsetText ??= subIfd.GetString(OffsetTimeOriginalTag);
                    break;
                case ExifIfd0Directory ifd0:
                    fallback ??= ifd0.GetString(ExifDirectoryBase.TagDateTime);
                    break;
            }
        }

        var captured = ParseExifDate(original) ?? ParseExifDate(fallback);
        if (captured is null)
        {
            return null;
        }

        var offset = TryParseOffset(offsetText);
        return new DateTimeOffset(
            DateTime.SpecifyKind(captured.Value, DateTimeKind.Unspecified),
            offset ?? TimeSpan.Zero);
    }

    private static DateTime? ParseExifDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParseExact(
            text.Trim(),
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
