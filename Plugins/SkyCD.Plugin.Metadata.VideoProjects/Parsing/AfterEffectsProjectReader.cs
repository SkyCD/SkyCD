using System.Buffers.Binary;
using System.Text;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class AfterEffectsProjectReader
{
    public static ProjectFacts? Read(Stream source, CancellationToken token)
    {
        Span<byte> header = stackalloc byte[12];
        if (source.ReadAtLeast(header, header.Length, false) != header.Length ||
            !header[..4].SequenceEqual("RIFX"u8) || !header[8..].SequenceEqual("Egg!"u8))
            return null;

        var facts = new AfterEffectsFacts("aep");
        var formEnd = 8L + BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (formEnd < 12)
            return null;

        var chunks = 0;
        facts.Facts.Truncated = formEnd > source.Length;
        if (!Walk(Math.Min(formEnd, source.Length), 0))
            facts.Facts.Truncated = true;
        return facts.Complete();

        bool Walk(long end, int depth)
        {
            if (depth > 96)
                return false;

            Span<byte> chunkHeader = stackalloc byte[8];
            Span<byte> data = stackalloc byte[0xa0];
            while (source.Position < end)
            {
                token.ThrowIfCancellationRequested();
                if (++chunks > 100_000 || end - source.Position < chunkHeader.Length)
                    return false;

                source.ReadExactly(chunkHeader);
                var size = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[4..]);
                var bodyEnd = source.Position + size;
                var next = bodyEnd + (size & 1);
                if (next > end)
                    return false;

                var tag = Encoding.ASCII.GetString(chunkHeader[..4]);
                if (tag == "LIST" && size >= 4)
                {
                    source.ReadExactly(data[..4]);
                    // These are the project item lists. Other lists can contain opaque data.
                    if ((data[..4].SequenceEqual("Fold"u8) || data[..4].SequenceEqual("Item"u8) ||
                        data[..4].SequenceEqual("Sfdr"u8)) && !Walk(bodyEnd, depth + 1))
                        return false;
                }
                else if (tag is "head" or "cdta" or "idta")
                {
                    var relevant = data[..(int)Math.Min(size, data.Length)];
                    source.ReadExactly(relevant);
                    facts.ReadChunk(tag, relevant);
                }

                source.Position = next;
            }
            return true;
        }
    }
}
