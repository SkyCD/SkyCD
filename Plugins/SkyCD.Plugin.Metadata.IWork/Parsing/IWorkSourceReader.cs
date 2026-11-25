using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.IWork.Exceptions;

namespace SkyCD.Plugin.Metadata.IWork.Parsing;

/// <summary>
/// Reads a bounded source into memory before any ZIP reader sees it.
/// </summary>
/// <remarks>
/// The plugin only ever holds bytes and never a path, so an iWork package cannot make it follow an external
/// reference. Reading in one pass also keeps the host's budget accounting simple: the charge equals the
/// document size, once.
/// </remarks>
internal static class IWorkSourceReader
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
            throw new IWorkDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new IWorkSourceTooLargeException(length, maxSourceBytes);
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
                throw new IWorkDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        if (buffer.Length < 4 || !StartsWithZipMagic(buffer))
        {
            // A modern iWork document is always a ZIP package, even when it is password protected: the
            // protection encrypts member payloads rather than the container. Anything else is damage.
            throw new IWorkDecodeException("the source does not start with a ZIP container header");
        }

        return buffer;
    }

    private static bool StartsWithZipMagic(byte[] data) =>
        data[0] == (byte)'P' && data[1] == (byte)'K' &&
        (data[2] == 0x03 && data[3] == 0x04
            || data[2] == 0x05 && data[3] == 0x06
            || data[2] == 0x07 && data[3] == 0x08);
}
