using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.MediaMetadata.Models;

/// <summary>
/// Reads a bounded prefix of a source in fixed chunks. Tag carriers sit in header directories and early
/// regions, so a cap keeps a scan cheap even on multi-gigabyte sources.
/// </summary>
internal static class SourcePrefix
{
    public const int ChunkBytes = 256 * 1024;

    public static async Task<byte[]> ReadAsync(IMediaSource source, long fallbackLength, int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var length = source.Length > 0 ? source.Length : fallbackLength;
        var target = (int)Math.Min(Math.Max(length, 0), maxBytes);
        if (target <= 0)
        {
            return [];
        }

        var buffer = new byte[target];
        var offset = 0;
        while (offset < target)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(ChunkBytes, target - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            chunk.Span.CopyTo(buffer.AsSpan(offset));
            offset += chunk.Length;
            if (chunk.Length != count)
            {
                // A short read means the source is smaller than advertised; scan what was actually read.
                Array.Resize(ref buffer, offset);
                break;
            }
        }

        return buffer;
    }
}
