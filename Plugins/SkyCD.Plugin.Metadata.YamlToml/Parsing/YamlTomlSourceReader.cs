using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.YamlToml.Exceptions;

namespace SkyCD.Plugin.Metadata.YamlToml.Parsing;

/// <summary>Reads a bounded source once into memory. The range is never requested again.</summary>
internal static class YamlTomlSourceReader
{
    private const int ChunkBytes = 256 * 1024;

    public static async Task<byte[]> ReadAsync(
        MediaItem item,
        IMediaSource source,
        long maxSourceBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length < 0)
        {
            length = 0;
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new YamlTomlSourceTooLargeException(length, maxSourceBytes);
        }

        if (length == 0)
        {
            return [];
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
                throw new InvalidOperationException(
                    $"The source returned {chunk.Length} of {count} requested bytes.");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        return buffer;
    }
}
