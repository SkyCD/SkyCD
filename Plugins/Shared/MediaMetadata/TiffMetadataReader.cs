using System;

namespace SkyCD.Plugin.MediaMetadata;

/// <summary>
/// Reads IFD0 and one Exif sub-directory of a TIFF container, which is also the payload of JPEG Exif and
/// the on-disk layout of TIFF-based RAW formats. Only text and rating tags are read; offsets are validated
/// against the bounded buffer, so a damaged directory yields no values instead of an exception.
/// </summary>
internal static class TiffMetadataReader
{
    private const ushort TagImageDescription = 0x010E;
    private const ushort TagXmp = 0x02BC;
    private const ushort TagRating = 0x4746;
    private const ushort TagRatingPercent = 0x4749;
    private const ushort TagIptc = 0x83BB;
    private const ushort TagExifIfd = 0x8769;
    private const ushort TagUserComment = 0x9286;
    private const ushort TagXpComment = 0x9C9C;
    private const ushort TagXpKeywords = 0x9C9E;

    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeUndefined = 7;

    private const long MaxValueBytes = 1 << 20;
    private const int MaxEntries = 512;

    public static bool LooksLike(ReadOnlySpan<byte> data) =>
        data.Length >= 8 &&
        ((data[0] == 0x49 && data[1] == 0x49) || (data[0] == 0x4D && data[1] == 0x4D));

    public static void Read(ReadOnlySpan<byte> data, MetadataScan scan)
    {
        if (!LooksLike(data))
        {
            return;
        }

        var littleEndian = data[0] == 0x49;
        if (ReadUInt16(data, 2, littleEndian) != 42)
        {
            return;
        }

        var ifdOffset = ReadOffset(data, 4, littleEndian);
        ReadDirectory(data, littleEndian, ifdOffset, scan, depth: 0);
    }

    private static void ReadDirectory(ReadOnlySpan<byte> data, bool littleEndian, int offset, MetadataScan scan,
        int depth)
    {
        // IFD0 plus a single Exif sub-directory; deeper chains are not needed for these tags.
        if (depth > 1 || offset <= 0 || offset + 2 > data.Length)
        {
            return;
        }

        var entries = ReadUInt16(data, offset, littleEndian);
        if (entries > MaxEntries)
        {
            return;
        }

        for (var index = 0; index < entries; index++)
        {
            var entry = offset + 2 + index * 12;
            if (entry + 12 > data.Length)
            {
                return;
            }

            var tag = ReadUInt16(data, entry, littleEndian);
            var type = ReadUInt16(data, entry + 2, littleEndian);
            var count = ReadOffset(data, entry + 4, littleEndian);
            var unitSize = TypeSize(type);
            if (unitSize == 0)
            {
                continue;
            }

            var byteCount = (long)unitSize * count;
            if (byteCount <= 0 || byteCount > MaxValueBytes)
            {
                continue;
            }

            if (tag == TagExifIfd && depth == 0)
            {
                ReadDirectory(data, littleEndian, ReadOffset(data, entry + 8, littleEndian), scan, depth + 1);
                continue;
            }

            var valueOffset = byteCount <= 4 ? entry + 8 : ReadOffset(data, entry + 8, littleEndian);
            if (valueOffset < 0 || valueOffset + byteCount > data.Length)
            {
                continue;
            }

            var value = data.Slice(valueOffset, (int)byteCount);
            switch (tag)
            {
                case TagImageDescription:
                    scan.SetComment(MetadataText.DecodeLatin1(value));
                    break;
                case TagXpComment:
                    scan.SetComment(MetadataText.DecodeUtf16LittleEndian(value));
                    break;
                case TagXpKeywords:
                    scan.AddKeywords(MetadataText.DecodeUtf16LittleEndian(value));
                    break;
                case TagUserComment:
                    scan.SetComment(DecodeUserComment(value));
                    break;
                case TagRating when type == TypeShort && count == 1:
                    scan.SetRating(ReadUInt16(value, 0, littleEndian));
                    break;
                case TagRatingPercent when type == TypeShort && count == 1:
                    scan.SetRating(ReadUInt16(value, 0, littleEndian));
                    break;
                case TagIptc when type is TypeByte or TypeUndefined:
                    IptcMetadataReader.ReadIim(value, scan);
                    break;
                case TagXmp when type is TypeByte or TypeUndefined:
                    XmpPacketReader.Read(value, scan);
                    break;
            }
        }
    }

    /// <summary>
    /// EXIF UserComment starts with an 8-byte character-code marker; tools that omit it write plain text,
    /// which is decoded leniently.
    /// </summary>
    private static string DecodeUserComment(ReadOnlySpan<byte> value)
    {
        if (value.Length >= 8)
        {
            var prefix = value[..8];
            if (prefix.StartsWith("ASCII\0\0\0"u8))
            {
                return MetadataText.Decode(value[8..]);
            }

            if (prefix.StartsWith("UNICODE\0"u8))
            {
                return MetadataText.DecodeUtf16LittleEndian(value[8..]);
            }

            if (prefix.SequenceEqual(new byte[8]))
            {
                return string.Empty;
            }
        }

        return MetadataText.Decode(value);
    }

    private static int TypeSize(int type) => type switch
    {
        TypeByte => 1,
        TypeAscii => 1,
        TypeShort => 2,
        TypeLong => 4,
        TypeUndefined => 1,
        _ => 0
    };

    private static int ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        if (offset < 0 || offset + 2 > data.Length)
        {
            return 0;
        }

        return littleEndian
            ? data[offset] | (data[offset + 1] << 8)
            : (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadOffset(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            return -1;
        }

        var value = littleEndian
            ? data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24)
            : (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        return value < 0 ? -1 : value;
    }
}
