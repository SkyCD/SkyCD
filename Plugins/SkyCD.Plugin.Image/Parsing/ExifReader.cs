using System;
using System.Globalization;
using System.Text;

namespace SkyCD.Plugin.Image.Parsing;

/// <summary>
/// Reads the EXIF capture date straight out of the container's TIFF directory.
/// </summary>
/// <remarks>
/// Only the tags that matter are walked — <c>DateTimeOriginal</c> with its <c>OffsetTimeOriginal</c>
/// companion, falling back to the IFD0 <c>DateTime</c> — so the plugin needs no general metadata library and
/// carries no transitive dependency whose license the host cannot verify. Every offset is bounds checked and
/// a malformed directory yields no date rather than a failure, because geometry already comes from the
/// decoder. EXIF stores local wall-clock time, so the offset tag is applied when present and UTC is assumed
/// otherwise; the offset travels with the value instead of the wall clock being silently shifted.
/// </remarks>
internal static class ExifReader
{
    /// <summary>EXIF dates are fixed-format ASCII, independent of any host or user culture.</summary>
    private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    private const ushort TagDateTime = 0x0132;
    private const ushort TagExifIfdPointer = 0x8769;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagOffsetTimeOriginal = 0x9011;
    private const ushort TiffTypeAscii = 2;
    private const ushort TiffTypeLong = 4;
    private const int MaxAsciiBytes = 64;

    public static DateTimeOffset? TryReadCapturedAt(ReadOnlySpan<byte> data)
    {
        var tiff = LocateTiffHeader(data);
        if (tiff < 0)
        {
            return null;
        }

        try
        {
            return ReadCapturedAt(data, tiff);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException
            or IndexOutOfRangeException or InvalidOperationException or OverflowException)
        {
            // A malformed directory must never fail an extraction that already has valid geometry.
            return null;
        }
    }

    private static DateTimeOffset? ReadCapturedAt(ReadOnlySpan<byte> data, int tiff)
    {
        if (!TryReadU16LittleEndian(data, tiff, out var byteOrderMark))
        {
            return null;
        }

        bool littleEndian;
        if (byteOrderMark == 0x4949)
        {
            littleEndian = true;
        }
        else if (byteOrderMark == 0x4D4D)
        {
            littleEndian = false;
        }
        else
        {
            return null;
        }

        if (!TryReadU16(data, tiff + 2, littleEndian, out var magic) || magic != 42)
        {
            return null;
        }

        if (!TryReadU32(data, tiff + 4, littleEndian, out var ifd0Offset))
        {
            return null;
        }

        var ifd0 = ScanIfd(data, tiff, tiff + (int)ifd0Offset, littleEndian);
        var exif = ifd0.ExifIfdOffset is { } pointer
            ? ScanIfd(data, tiff, tiff + (int)pointer, littleEndian)
            : default;

        var captured = ParseExifDate(exif.DateTimeOriginal) ?? ParseExifDate(ifd0.DateTime);
        if (captured is null)
        {
            return null;
        }

        var offset = TryParseOffset(exif.OffsetTimeOriginal);
        return new DateTimeOffset(
            DateTime.SpecifyKind(captured.Value, DateTimeKind.Unspecified),
            offset ?? TimeSpan.Zero);
    }

    private static IfdScan ScanIfd(ReadOnlySpan<byte> data, int tiff, int ifd, bool littleEndian)
    {
        if (!TryReadU16(data, ifd, littleEndian, out var entryCount))
        {
            return default;
        }

        string? dateTime = null;
        string? dateTimeOriginal = null;
        string? offsetTimeOriginal = null;
        uint? exifIfdOffset = null;

        for (var index = 0; index < entryCount; index++)
        {
            var entry = ifd + 2 + (index * 12);
            if (!TryReadU16(data, entry, littleEndian, out var tag) ||
                !TryReadU16(data, entry + 2, littleEndian, out var type) ||
                !TryReadU32(data, entry + 4, littleEndian, out var count) ||
                !TryReadU32(data, entry + 8, littleEndian, out var valueOrOffset))
            {
                break;
            }

            if (tag == TagExifIfdPointer && type == TiffTypeLong)
            {
                exifIfdOffset ??= valueOrOffset;
                continue;
            }

            if (type != TiffTypeAscii)
            {
                continue;
            }

            // Values of four bytes or fewer live inline; longer ones are pointed at from the TIFF body.
            var start = count <= 4 ? entry + 8 : tiff + (int)valueOrOffset;
            var text = ReadAscii(data, start, (int)Math.Min(count, MaxAsciiBytes));

            switch (tag)
            {
                case TagDateTime:
                    dateTime ??= text;
                    break;
                case TagDateTimeOriginal:
                    dateTimeOriginal ??= text;
                    break;
                case TagOffsetTimeOriginal:
                    offsetTimeOriginal ??= text;
                    break;
            }
        }

        return new IfdScan(dateTime, dateTimeOriginal, offsetTimeOriginal, exifIfdOffset);
    }

    private readonly record struct IfdScan(
        string? DateTime,
        string? DateTimeOriginal,
        string? OffsetTimeOriginal,
        uint? ExifIfdOffset);

    /// <summary>Finds the TIFF header inside JPEG APP1, PNG eXIf, WebP EXIF or a bare TIFF container.</summary>
    private static int LocateTiffHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return -1;
        }

        if (IsTiff(data, 0))
        {
            return 0;
        }

        if (data[0] == 0xFF && data[1] == 0xD8)
        {
            return FindJpegExif(data);
        }

        if (data.StartsWith("\x89PNG\r\n\x1A\n"u8))
        {
            return FindPngExif(data);
        }

        if (data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return FindWebpExif(data);
        }

        return -1;
    }

    private static int FindJpegExif(ReadOnlySpan<byte> data)
    {
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
            {
                return -1;
            }

            var marker = data[position + 1];

            // Standalone markers carry no length field.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                position += 2;
                continue;
            }

            // Entropy-coded data starts here; nothing after it can hold an EXIF segment.
            if (marker == 0xDA)
            {
                return -1;
            }

            if (!TryReadU16BigEndian(data, position + 2, out var segmentLength) || segmentLength < 2)
            {
                return -1;
            }

            if (marker == 0xE1)
            {
                var payload = position + 4;
                if (payload + 6 <= data.Length &&
                    data.Slice(payload, 6).SequenceEqual("Exif\0\0"u8) &&
                    IsTiff(data, payload + 6))
                {
                    return payload + 6;
                }
            }

            position += 2 + segmentLength;
        }

        return -1;
    }

    private static int FindPngExif(ReadOnlySpan<byte> data)
    {
        var position = 8;
        while (position + 12 <= data.Length)
        {
            if (!TryReadU32BigEndian(data, position, out var chunkLength))
            {
                return -1;
            }

            var type = data.Slice(position + 4, 4);
            if (type.SequenceEqual("eXIf"u8))
            {
                var payload = position + 8;
                return IsTiff(data, payload) ? payload : -1;
            }

            if (type.SequenceEqual("IEND"u8))
            {
                return -1;
            }

            position += 12 + (int)chunkLength;
        }

        return -1;
    }

    private static int FindWebpExif(ReadOnlySpan<byte> data)
    {
        var position = 12;
        while (position + 8 <= data.Length)
        {
            var fourCc = data.Slice(position, 4);
            if (!TryReadU32LittleEndian(data, position + 4, out var chunkLength))
            {
                return -1;
            }

            var payload = position + 8;
            if (fourCc.SequenceEqual("EXIF"u8))
            {
                // Some writers include the "Exif\0\0" prefix, others start straight at the TIFF header.
                if (payload + 6 <= data.Length && data.Slice(payload, 6).SequenceEqual("Exif\0\0"u8))
                {
                    payload += 6;
                }

                return IsTiff(data, payload) ? payload : -1;
            }

            // RIFF chunks are padded to an even size.
            position = payload + (int)chunkLength + (chunkLength % 2 == 1 ? 1 : 0);
        }

        return -1;
    }

    private static bool IsTiff(ReadOnlySpan<byte> data, int offset) =>
        offset >= 0 && offset + 4 <= data.Length &&
        ((data[offset] == (byte)'I' && data[offset + 1] == (byte)'I' && data[offset + 2] == 42 && data[offset + 3] == 0) ||
         (data[offset] == (byte)'M' && data[offset + 1] == (byte)'M' && data[offset + 2] == 0 && data[offset + 3] == 42));

    private static string? ReadAscii(ReadOnlySpan<byte> data, int start, int count)
    {
        if (count <= 0 || start < 0 || start + count > data.Length)
        {
            return null;
        }

        var slice = data.Slice(start, count);
        var terminator = slice.IndexOf((byte)0);
        if (terminator >= 0)
        {
            slice = slice[..terminator];
        }

        var text = Encoding.ASCII.GetString(slice).Trim();
        return text.Length == 0 ? null : text;
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

    private static bool TryReadU16(ReadOnlySpan<byte> data, int offset, bool littleEndian, out ushort value) =>
        littleEndian
            ? TryReadU16LittleEndian(data, offset, out value)
            : TryReadU16BigEndian(data, offset, out value);

    private static bool TryReadU16LittleEndian(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        value = 0;
        if (offset < 0 || offset + 2 > data.Length)
        {
            return false;
        }

        value = (ushort)(data[offset] | (data[offset + 1] << 8));
        return true;
    }

    private static bool TryReadU16BigEndian(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        value = 0;
        if (offset < 0 || offset + 2 > data.Length)
        {
            return false;
        }

        value = (ushort)((data[offset] << 8) | data[offset + 1]);
        return true;
    }

    private static bool TryReadU32(ReadOnlySpan<byte> data, int offset, bool littleEndian, out uint value) =>
        littleEndian
            ? TryReadU32LittleEndian(data, offset, out value)
            : TryReadU32BigEndian(data, offset, out value);

    private static bool TryReadU32LittleEndian(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        return true;
    }

    private static bool TryReadU32BigEndian(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > data.Length)
        {
            return false;
        }

        value = (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        return true;
    }
}
