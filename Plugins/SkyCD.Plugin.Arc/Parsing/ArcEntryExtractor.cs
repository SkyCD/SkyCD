using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Arc.Exceptions;
using SkyCD.Plugin.Arc.Models;

namespace SkyCD.Plugin.Arc.Parsing;

/// <summary>
/// Copies one stored entry out of an archive stream. The copy is bounded by the configured per-entry limit so a
/// damaged header cannot cause unbounded writes.
/// </summary>
internal static class ArcEntryExtractor
{
    private const int CopyBufferBytes = 81_920;

    /// <summary>
    /// Reads <paramref name="entry"/>'s stored bytes from <paramref name="archive"/> into
    /// <paramref name="destination"/>. The archive stream must be seekable.
    /// </summary>
    public static async Task CopyStoredAsync(
        Stream archive,
        ArcEntry entry,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);

        archive.Position = entry.DataOffset;
        var remaining = entry.CompressedSize;
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        try
        {
            long written = 0;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await archive.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new ArcDamagedArchiveException();
                }

                written += read;
                if (written > maxBytes)
                {
                    throw new ArcEntryLimitExceededException();
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
