using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Image.Exceptions;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Reads a bounded source into memory before any native decoder sees it.
/// </summary>
/// <remarks>
/// This is deliberate rather than convenient. Skia calls back into managed code to pull bytes from a
/// managed stream, and an exception thrown inside that callback cannot cross the native boundary — it
/// terminates the process instead of failing the provider. Reading first means the host's budget exception
/// is raised on this plugin's own stack, where it becomes an ordinary provider failure.
/// </remarks>
internal static class ImageSourceReader
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
            throw new ImageDecodeException("the source is empty");
        }

        if (length > maxSourceBytes)
        {
            throw new ImageSourceTooLargeException(length, maxSourceBytes);
        }

        if (length > int.MaxValue)
        {
            throw new ImageSourceTooLargeException(length, maxSourceBytes);
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
                throw new ImageDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        return buffer;
    }
}
