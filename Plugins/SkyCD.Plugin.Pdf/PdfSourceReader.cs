using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Pdf.Exceptions;

namespace SkyCD.Plugin.Pdf;

/// <summary>
/// Reads a bounded source into memory before any PDF library sees it.
/// </summary>
/// <remarks>
/// Both the parser and the renderer take a byte array rather than a stream, and reading in one pass keeps the
/// host's budget accounting simple: the charge equals the document size, once. Because the plugin only ever
/// holds bytes and never a path, it cannot follow an external file reference even if a document asks it to.
/// </remarks>
internal static class PdfSourceReader
{
    private const int ChunkBytes = 256 * 1024;

    public static async Task<byte[]> ReadAllAsync(
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
            throw new PdfDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new PdfSourceTooLargeException(length, maxSourceBytes);
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
                throw new PdfDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        if (buffer.Length < 5 || !StartsWithPdfMagic(buffer))
        {
            throw new PdfDecodeException("the source does not start with a PDF header");
        }

        return buffer;
    }

    private static bool StartsWithPdfMagic(byte[] data) =>
        data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F' && data[4] == (byte)'-';
}
