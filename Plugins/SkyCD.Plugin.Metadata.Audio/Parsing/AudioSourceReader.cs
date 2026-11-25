using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Audio.Exceptions;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Bounded random-access reader over the host's <see cref="IMediaSource"/>.
/// </summary>
/// <remarks>
/// Audio tags live at the head and tail of a file while the file itself can be hundreds of megabytes, so
/// readers ask for exact ranges instead of a stream. Requests are clamped to the source length — the host
/// rejects a range that reaches past the end — and a read that exhausts the host's byte budget surfaces the
/// host's own exception on this plugin's stack, where it becomes an ordinary provider failure.
/// </remarks>
internal sealed class AudioSourceReader
{
    private readonly IMediaSource source;

    public AudioSourceReader(IMediaSource source, MediaItem item)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(item);

        Length = source.Length > 0 ? source.Length : item.Length;
        if (Length <= 0)
        {
            throw new AudioDecodeException("the source is empty");
        }
    }

    public long Length { get; }

    /// <summary>Reads up to <paramref name="count"/> bytes, returning fewer only at the end of the source.</summary>
    public async Task<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (offset < 0 || offset >= Length || count <= 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var clamped = (int)Math.Min(count, Length - offset);
        return await source.ReadAsync(offset, clamped, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes, or fewer if the source ends first.</summary>
    public async Task<byte[]> ReadBytesAsync(long offset, int count, CancellationToken cancellationToken)
    {
        var memory = await ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    /// <summary>Reads the final <paramref name="count"/> bytes, where ID3v1 and MP4 metadata often live.</summary>
    public Task<byte[]> ReadTailAsync(int count, CancellationToken cancellationToken)
    {
        var size = (int)Math.Min(count, Length);
        return ReadBytesAsync(Length - size, size, cancellationToken);
    }

    /// <summary>Scans forward for a marker, reading in bounded chunks. Returns -1 when absent.</summary>
    public async Task<long> IndexOfAsync(
        long start,
        ReadOnlyMemory<byte> marker,
        int searchBytes,
        CancellationToken cancellationToken)
    {
        var limit = Math.Min(Length, start + searchBytes);
        var position = start;
        var carry = ReadOnlyMemory<byte>.Empty;

        while (position < limit)
        {
            var chunkSize = (int)Math.Min(64 * 1024, limit - position);
            var chunk = await ReadAsync(position, chunkSize, cancellationToken).ConfigureAwait(false);
            if (chunk.IsEmpty)
            {
                return -1;
            }

            // The carry overlaps the previous chunk so a marker straddling a boundary is still found.
            var window = carry.IsEmpty ? chunk : Merge(carry, chunk);
            var index = window.Span.IndexOf(marker.Span);
            if (index >= 0)
            {
                return position - carry.Length + index;
            }

            carry = chunk.Length <= marker.Length
                ? chunk
                : chunk.Slice(chunk.Length - marker.Length);
            position += chunk.Length;
        }

        return -1;
    }

    private static ReadOnlyMemory<byte> Merge(ReadOnlyMemory<byte> carry, ReadOnlyMemory<byte> chunk)
    {
        var merged = new byte[carry.Length + chunk.Length];
        carry.CopyTo(merged);
        chunk.CopyTo(merged.AsMemory(carry.Length));
        return merged;
    }
}
