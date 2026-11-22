using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image.Tests;

/// <summary>
/// In-memory <see cref="IMediaSource"/> that charges reads exactly like the host does, so tests exercise the
/// same bounded random-access contract a plugin sees in production.
/// </summary>
internal sealed class ByteArrayMediaSource : IMediaSource
{
    private readonly byte[] data;
    private readonly long budget;
    private long charged;

    public ByteArrayMediaSource(byte[] data, long budget = 16 * 1024 * 1024)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.budget = budget;
    }

    /// <summary>Bytes charged against the budget so far.</summary>
    public long ChargedBytes => charged;

    /// <summary>Number of read calls, used to prove the stream adapter batches instead of reading per byte.</summary>
    public int ReadCount { get; private set; }

    public long Length => data.Length;

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;

        if (offset < 0 || count < 0 || offset > Length || count > Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The requested range is outside the source.");
        }

        if (count > budget - charged)
        {
            throw new InvalidOperationException("The source read budget is exhausted.");
        }

        charged += count;
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(new ReadOnlyMemory<byte>(data, (int)offset, count));
    }
}
