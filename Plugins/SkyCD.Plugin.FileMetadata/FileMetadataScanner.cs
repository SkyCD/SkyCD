using System;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Chooses the container reader by magic bytes rather than extension, because tools happily write a TIFF
/// payload under a RAW name or keep an XMP packet in a file named after something else. Anything that is
/// not a TIFF container is treated as an XMP sidecar, which is exactly what a bare <c>.xmp</c> file is.
/// </summary>
internal static class FileMetadataScanner
{
    public static MetadataScan Scan(ReadOnlySpan<byte> data)
    {
        var scan = new MetadataScan();
        if (TiffMetadataReader.LooksLike(data))
        {
            TiffMetadataReader.Read(data, scan);
        }
        else
        {
            XmpPacketReader.Read(data, scan);
        }

        return scan;
    }
}
