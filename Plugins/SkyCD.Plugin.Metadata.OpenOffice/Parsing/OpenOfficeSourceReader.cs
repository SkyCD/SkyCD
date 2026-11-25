using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.OpenOffice.Exceptions;

namespace SkyCD.Plugin.Metadata.OpenOffice.Parsing;

/// <summary>
/// Reads a bounded source into memory before any ZIP reader sees it.
/// </summary>
/// <remarks>
/// Reading in one pass keeps the host's budget accounting simple: the charge equals the source size, once.
/// The plugin only ever holds bytes and never a path, so a document that references another file has no way
/// to make it open that file.
/// </remarks>
internal static class OpenOfficeSourceReader
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
            throw new OpenOfficeDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new OpenOfficeSourceTooLargeException(length, maxSourceBytes);
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
                throw new OpenOfficeDecodeException(
                    $"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        if (buffer.Length < 4 || !StartsWithZipMagic(buffer))
        {
            throw new OpenOfficeDecodeException("the source does not start with a ZIP container header");
        }

        return buffer;
    }

    private static bool StartsWithZipMagic(byte[] data) =>
        data[0] == (byte)'P' && data[1] == (byte)'K' &&
        (data[2] == 0x03 && data[3] == 0x04
            || data[2] == 0x05 && data[3] == 0x06
            || data[2] == 0x07 && data[3] == 0x08);
}
