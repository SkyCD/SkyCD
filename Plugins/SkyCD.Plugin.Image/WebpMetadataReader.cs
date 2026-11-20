using System;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.Image;

/// <summary>Walks the RIFF chunks of a WebP file for its XMP and EXIF payloads.</summary>
internal static class WebpMetadataReader
{
    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;
    private static ReadOnlySpan<byte> WebpSignature => "WEBP"u8;
    private static ReadOnlySpan<byte> XmpChunk => "XMP "u8;
    private static ReadOnlySpan<byte> ExifChunk => "EXIF"u8;

    public static bool LooksLike(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[..4].SequenceEqual(RiffSignature) && data[8..12].SequenceEqual(WebpSignature);

    public static void Read(ReadOnlySpan<byte> data, MetadataScan scan)
    {
        var position = 12;
        while (position + 8 <= data.Length)
        {
            var size = ReadInt32LittleEndian(data, position + 4);
            if (size < 0 || position + 8 + size > data.Length)
            {
                return;
            }

            var type = data.Slice(position, 4);
            var payload = data.Slice(position + 8, size);
            if (type.SequenceEqual(XmpChunk))
            {
                XmpPacketReader.Read(payload, scan);
            }
            else if (type.SequenceEqual(ExifChunk))
            {
                var tiff = payload.StartsWith("Exif\0\0"u8) ? payload[6..] : payload;
                TiffMetadataReader.Read(tiff, scan);
            }

            position += 8 + size + (size & 1);
        }
    }

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
