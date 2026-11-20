using System;
using System.Text;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Finds the XMP packet inside a file's prefix. XMP is written either as a wrapped packet
/// (<c>&lt;?xpacket ...?&gt;</c> around <c>x:xmpmeta</c>) or, by some tools, as bare RDF. The locator only
/// slices bytes; the XML is parsed by <see cref="XmpMetadataReader"/>, which never resolves entities or DTDs.
/// </summary>
internal static class XmpPacketLocator
{
    private static readonly byte[] PacketStart = Encoding.ASCII.GetBytes("<?xpacket");
    private static readonly byte[] PacketEnd = Encoding.ASCII.GetBytes("<?xpacket end");
    private static readonly byte[] XmpMetaStart = Encoding.ASCII.GetBytes("<x:xmpmeta");
    private static readonly byte[] XmpMetaEnd = Encoding.ASCII.GetBytes("</x:xmpmeta>");
    private static readonly byte[] RdfStart = Encoding.ASCII.GetBytes("<rdf:RDF");
    private static readonly byte[] RdfEnd = Encoding.ASCII.GetBytes("</rdf:RDF>");
    private static readonly byte[] ProcessingInstructionEnd = Encoding.ASCII.GetBytes("?>");

    /// <summary>Locates the first packet in <paramref name="data"/>; returns false when there is none.</summary>
    public static bool TryLocate(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> packet)
    {
        packet = default;
        if (data.IsEmpty)
        {
            return false;
        }

        var packetStart = data.IndexOf(PacketStart);
        var metaStart = data.IndexOf(XmpMetaStart);
        var rdfStart = data.IndexOf(RdfStart);

        if (packetStart >= 0 && (metaStart < 0 || packetStart <= metaStart))
        {
            var end = data.Slice(packetStart).IndexOf(PacketEnd);
            if (end >= 0)
            {
                var tail = data[(packetStart + end)..];
                var close = tail.IndexOf(ProcessingInstructionEnd);
                var length = close >= 0 ? end + close + ProcessingInstructionEnd.Length : tail.Length;
                packet = data.Slice(packetStart, Math.Min(length, data.Length - packetStart));
            }
            else
            {
                // A truncated packet still carries usable properties up to the cut; the parser decides.
                packet = data[packetStart..];
            }

            return true;
        }

        if (metaStart >= 0)
        {
            packet = SliceToEnd(data, metaStart, XmpMetaEnd);
            return true;
        }

        if (rdfStart >= 0)
        {
            packet = SliceToEnd(data, rdfStart, RdfEnd);
            return true;
        }

        return false;
    }

    private static ReadOnlySpan<byte> SliceToEnd(ReadOnlySpan<byte> data, int start, ReadOnlySpan<byte> endMarker)
    {
        var from = data[start..];
        var end = from.IndexOf(endMarker);
        return end >= 0 ? from[..(end + endMarker.Length)] : from;
    }
}
