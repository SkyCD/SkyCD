using System;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Chooses the container reader by magic bytes rather than extension, because tools happily write a JPEG
/// payload into a <c>.tif</c> or an XMP sidecar next to a RAW file. Anything unrecognized is treated as an
/// XMP sidecar, which is exactly what a bare <c>.xmp</c> file is.
/// </summary>
internal static class FileMetadataScanner
{
    public static MetadataScan Scan(ReadOnlySpan<byte> data)
    {
        var scan = new MetadataScan();
        if (JpegMetadataReader.LooksLike(data))
        {
            JpegMetadataReader.Read(data, scan);
        }
        else if (PngMetadataReader.LooksLike(data))
        {
            PngMetadataReader.Read(data, scan);
        }
        else if (TiffMetadataReader.LooksLike(data))
        {
            TiffMetadataReader.Read(data, scan);
        }
        else if (WebpMetadataReader.LooksLike(data))
        {
            WebpMetadataReader.Read(data, scan);
        }
        else
        {
            XmpPacketReader.Read(data, scan);
        }

        return scan;
    }
}
