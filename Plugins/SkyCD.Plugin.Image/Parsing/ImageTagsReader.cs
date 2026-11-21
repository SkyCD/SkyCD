using System;
using SkyCD.Plugin.MediaMetadata;
using SkyCD.Plugin.MediaMetadata.Parsing;

namespace SkyCD.Plugin.Image.Parsing;

/// <summary>
/// Reads the keywords, comment and rating embedded in the image containers this plugin presents. The reader
/// is chosen by magic bytes rather than extension, because a re-encoded image may keep its old name. GIF
/// carries no portable metadata and yields nothing.
/// </summary>
internal static class ImageTagsReader
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
        else if (WebpMetadataReader.LooksLike(data))
        {
            WebpMetadataReader.Read(data, scan);
        }

        return scan;
    }
}
