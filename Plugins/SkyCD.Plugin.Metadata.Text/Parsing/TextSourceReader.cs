using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Text.Exceptions;
using SkyCD.Plugin.Metadata.Text.Models;

namespace SkyCD.Plugin.Metadata.Text.Parsing;

/// <summary>The leading window read from a source, and whether it was the whole file.</summary>
internal sealed record TextWindow(byte[] Bytes, bool ScanComplete, long SourceLength);

/// <summary>
/// Reads a bounded leading window of a source.
/// </summary>
/// <remarks>
/// A text excerpt needs the beginning of a file, not all of it, so the scan window is independent of the
/// source size: a multi-gigabyte log costs one window read, and <see cref="TextWindow.ScanComplete"/> records
/// that the rest was never decoded. Only <see cref="TextSchema.ByteLength"/> reflects the whole file.
/// </remarks>
internal static class TextSourceReader
{
    private const int ChunkBytes = 64 * 1024;

    public static async Task<TextWindow> ReadWindowAsync(
        MediaItem item,
        IMediaSource source,
        TextPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length < 0)
        {
            throw new TextDecodeException("the source reports a negative length");
        }

        if (length == 0)
        {
            // An empty file is a legitimate empty text document, not a failure; the analyzer reports zero
            // lines and zero characters, and the preview capability then produces no artifact.
            return new TextWindow([], ScanComplete: true, SourceLength: 0);
        }

        if (length > options.MaxSourceBytes)
        {
            throw new TextSourceTooLargeException(length, options.MaxSourceBytes);
        }

        var windowLength = (int)Math.Min(length, options.MaxScanBytes);
        var buffer = new byte[windowLength];
        var offset = 0;

        while (offset < windowLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(ChunkBytes, windowLength - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new TextDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan(offset, count));
            offset += count;
        }

        return new TextWindow(buffer, windowLength == length, length);
    }
}
