using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Executable.Exceptions;

namespace SkyCD.Plugin.Executable;

/// <summary>
/// Random access to a bounded executable source. Every read is range-checked against the source length and the
/// size the plugin is willing to inspect, so a malformed header cannot make a caller fetch bytes it should not
/// see, and every fetched byte is charged to the host's budget exactly once.
/// </summary>
internal sealed class ExecutableSource
{
    private const int ChunkBytes = 256 * 1024;

    private readonly IMediaSource source;
    private readonly long limit;

    public ExecutableSource(IMediaSource source, long limit)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.limit = Math.Min(Math.Min(source.Length, limit), int.MaxValue);
    }

    /// <summary>The number of bytes this reader may address.</summary>
    public long Length => limit;

    /// <summary>Reads exactly <paramref name="length"/> bytes at <paramref name="offset"/>.</summary>
    public async Task<byte[]> ReadAsync(long offset, int length, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (offset < 0 || offset > limit || length > limit - offset)
        {
            throw new ExecutableReadException("the requested range lies outside the readable image");
        }

        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(ChunkBytes, length - read);
            var chunk = await source.ReadAsync(offset + read, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new ExecutableReadException(
                    $"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan(read, count));
            read += count;
        }

        return buffer;
    }
}
