using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Rtf.Exceptions;

namespace SkyCD.Plugin.Metadata.Rtf.Parsing;

/// <summary>
/// Reads a bounded RTF source into memory and verifies its header before any parser sees it.
/// </summary>
/// <remarks>
/// The plugin is handed bytes rather than a path, so embedded file references have nothing to resolve
/// against. Bytes are decoded as ISO-8859-1 because RTF is a seven-bit control language: every byte maps to
/// one character, and RTF's own escaped sequences carry the real encoding.
/// </remarks>
internal static class RtfSourceReader
{
    private const int ChunkBytes = 256 * 1024;

    public static async Task<string> ReadTextAsync(
        MediaItem item,
        IMediaSource source,
        long maxSourceBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new RtfDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new RtfSourceTooLargeException(length, maxSourceBytes);
        }

        var buffer = new byte[length];
        var offset = 0L;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = (int)Math.Min(ChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new RtfDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        var text = Encoding.Latin1.GetString(buffer);
        if (!HasRtfSignature(text))
        {
            throw new RtfDecodeException("the source does not start with an RTF header");
        }

        return text;
    }

    private static bool HasRtfSignature(string text)
    {
        var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase);
    }
}
