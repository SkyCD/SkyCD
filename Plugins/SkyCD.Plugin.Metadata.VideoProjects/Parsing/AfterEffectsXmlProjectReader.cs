using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class AfterEffectsXmlProjectReader
{
    public static ProjectFacts? Read(Stream source, CancellationToken token)
    {
        var root = ProjectXml.Read(source, 64 * 1024 * 1024, token);
        if (root?.Name != "AfterEffectsProject")
            return null;

        var facts = new AfterEffectsFacts("aepx");
        Span<byte> data = stackalloc byte[0xa0];
        var chunks = 0;
        foreach (var element in root.Descendants())
        {
            token.ThrowIfCancellationRequested();
            if (++chunks > 100_000)
            {
                facts.Facts.Truncated = true;
                break;
            }
            var tag = element.Name.LocalName;
            if (tag is not ("head" or "cdta" or "idta") || (string?)element.Attribute("bdata") is not { } hex)
                continue;

            var length = Math.Min(hex.Length / 2, data.Length);
            if (hex.Length % 2 == 0 &&
                Convert.FromHexString(hex.AsSpan(0, length * 2), data, out _, out var bytesWritten) ==
                System.Buffers.OperationStatus.Done)
                facts.ReadChunk(tag, data[..bytesWritten]);
        }
        return facts.Complete();
    }
}
