using System;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Walks JPEG marker segments for the four metadata carriers that are portable between tools: an APP1 Exif
/// TIFF block, an APP1 XMP packet, an APP13 Photoshop/IPTC block and a COM comment. The image data after
/// Start-Of-Scan is never read.
/// </summary>
internal static class JpegMetadataReader
{
    private const byte MarkerPrefix = 0xFF;
    private const byte MarkerTem = 0x01;
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerCom = 0xFE;
    private const byte MarkerApp1 = 0xE1;
    private const byte MarkerApp13 = 0xED;

    private static ReadOnlySpan<byte> ExifSignature => "Exif\0\0"u8;
    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;
    private static ReadOnlySpan<byte> PhotoshopSignature => "Photoshop 3.0\0"u8;

    public static bool LooksLike(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && data[0] == MarkerPrefix && data[1] == MarkerSoi;

    public static void Read(ReadOnlySpan<byte> data, MetadataScan scan)
    {
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != MarkerPrefix)
            {
                // Recover from corrupt bytes before the next marker.
                position++;
                continue;
            }

            var marker = data[position + 1];
            if (marker == MarkerPrefix || marker == 0x00)
            {
                position++;
                continue;
            }

            if (marker == MarkerSoi || marker == MarkerTem || marker is >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            if (marker == MarkerSos || marker == MarkerEoi)
            {
                return;
            }

            var length = (data[position + 2] << 8) | data[position + 3];
            if (length < 2 || position + 2 + length > data.Length)
            {
                return;
            }

            var payload = data.Slice(position + 4, length - 2);
            switch (marker)
            {
                case MarkerApp1 when payload.StartsWith(ExifSignature):
                    TiffMetadataReader.Read(payload[ExifSignature.Length..], scan);
                    break;
                case MarkerApp1 when payload.StartsWith(XmpSignature):
                    XmpPacketReader.Read(payload[XmpSignature.Length..], scan);
                    break;
                case MarkerApp13 when payload.StartsWith(PhotoshopSignature):
                    IptcMetadataReader.ReadPhotoshopResources(payload[PhotoshopSignature.Length..], scan);
                    break;
                case MarkerCom:
                    scan.SetComment(MetadataText.DecodeLatin1(payload));
                    break;
            }

            position += 2 + length;
        }
    }
}
