using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Video.Exceptions;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Bounded random-access reader over the host's <see cref="IMediaSource"/>.
/// </summary>
/// <remarks>
/// Video containers are gigabytes while their metadata is kilobytes, so readers walk atom and element trees
/// by reading small headers and skipping payload by offset rather than streaming. The host charges only the
/// bytes actually read, which is what makes a trailing MP4 <c>moov</c> box or a Matroska seek head affordable.
/// A read that exhausts the host's budget surfaces the host's own exception on this plugin's stack, where it
/// becomes an ordinary provider failure.
/// </remarks>
internal sealed class VideoSourceReader
{
    private readonly IMediaSource source;

    public VideoSourceReader(IMediaSource source, MediaItem item)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(item);

        Length = source.Length > 0 ? source.Length : item.Length;
        if (Length <= 0)
        {
            throw new VideoDecodeException("the source is empty");
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

    public async Task<byte[]> ReadBytesAsync(long offset, int count, CancellationToken cancellationToken)
    {
        var memory = await ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    /// <summary>Reads the final <paramref name="count"/> bytes, where a trailing MP4 <c>moov</c> often lives.</summary>
    public Task<byte[]> ReadTailAsync(int count, CancellationToken cancellationToken)
    {
        var size = (int)Math.Min(count, Length);
        return ReadBytesAsync(Length - size, size, cancellationToken);
    }
}
