using System;
using SkyCD.Plugin.MediaMetadata;
using SkyCD.Plugin.MediaMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.Image.Parsing;

/// <summary>
/// Recognizes the TIFF container by magic bytes rather than extension: tools store a TIFF payload under RAW
/// names, so the extension never identifies the layout. XMP embedded in a TIFF directory is read as tag 700
/// by the container reader itself.
/// </summary>
internal static class ContainerTagsReader
{
    public static MetadataScan Scan(ReadOnlySpan<byte> data)
    {
        var scan = new MetadataScan();
        if (TiffMetadataReader.LooksLike(data))
        {
            TiffMetadataReader.Read(data, scan);
        }

        return scan;
    }
}
