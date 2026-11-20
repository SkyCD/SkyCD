using System;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Walks PNG chunks for the text chunks that image tools use for keywords, comments and XMP, and for the
/// <c>eXIf</c> chunk that carries a TIFF directory. Compressed <c>zTXt</c> and <c>iTXt</c> text is skipped:
/// decompressing metadata would cost more CPU than a bounded index pass can afford.
/// </summary>
internal static class PngMetadataReader
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> TextChunk => "tEXt"u8;
    private static ReadOnlySpan<byte> InternationalTextChunk => "iTXt"u8;
    private static ReadOnlySpan<byte> ExifChunk => "eXIf"u8;
    private static ReadOnlySpan<byte> EndChunk => "IEND"u8;

    public static bool LooksLike(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(Signature);

    public static void Read(ReadOnlySpan<byte> data, MetadataScan scan)
    {
        var position = 8;
        while (position + 12 <= data.Length)
        {
            var length = ReadInt32BigEndian(data, position);
            if (length < 0 || position + 12 + length > data.Length)
            {
                return;
            }

            var type = data.Slice(position + 4, 4);
            var payload = data.Slice(position + 8, length);

            if (type.SequenceEqual(EndChunk))
            {
                return;
            }

            if (type.SequenceEqual(TextChunk))
            {
                ReadText(payload, scan);
            }
            else if (type.SequenceEqual(InternationalTextChunk))
            {
                ReadInternationalText(payload, scan);
            }
            else if (type.SequenceEqual(ExifChunk))
            {
                TiffMetadataReader.Read(payload, scan);
            }

            position += 12 + length;
        }
    }

    private static void ReadText(ReadOnlySpan<byte> payload, MetadataScan scan)
    {
        var separator = payload.IndexOf((byte)0);
        if (separator <= 0)
        {
            return;
        }

        var keyword = MetadataText.DecodeLatin1(payload[..separator]);
        var text = MetadataText.DecodeLatin1(payload[(separator + 1)..]);
        ApplyText(keyword, text, scan);
    }

    private static void ReadInternationalText(ReadOnlySpan<byte> payload, MetadataScan scan)
    {
        var separator = payload.IndexOf((byte)0);
        if (separator <= 0 || separator + 3 > payload.Length)
        {
            return;
        }

        var keyword = MetadataText.DecodeLatin1(payload[..separator]);
        var compressed = payload[separator + 1] != 0;
        var rest = payload[(separator + 3)..];

        var languageEnd = rest.IndexOf((byte)0);
        if (languageEnd < 0)
        {
            return;
        }

        rest = rest[(languageEnd + 1)..];
        var translatedEnd = rest.IndexOf((byte)0);
        if (translatedEnd < 0)
        {
            return;
        }

        var text = rest[(translatedEnd + 1)..];
        if (keyword.Equals("XML:com.adobe.xmp", StringComparison.Ordinal))
        {
            if (!compressed)
            {
                XmpPacketReader.Read(text, scan);
            }

            return;
        }

        if (!compressed)
        {
            ApplyText(keyword, MetadataText.Decode(text), scan);
        }
    }

    private static void ApplyText(string keyword, string text, MetadataScan scan)
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

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
