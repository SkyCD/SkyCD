using System;

namespace SkyCD.Plugin.MediaMetadata;

/// <summary>Locates and merges an XMP packet into the scan; used by every container reader.</summary>
internal static class XmpPacketReader
{
    public static void Read(ReadOnlySpan<byte> data, MetadataScan scan)
    {
        if (!XmpPacketLocator.TryLocate(data, out var packet) ||
            !XmpMetadataReader.TryRead(packet, out var metadata))
        {
            return;
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
