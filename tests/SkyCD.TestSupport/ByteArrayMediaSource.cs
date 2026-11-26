using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;

namespace SkyCD.TestSupport;

/// <summary>
/// In-memory <see cref="IMediaSource"/> that charges reads exactly like the host does, so tests exercise the
/// same bounded random-access contract a plugin sees in production.
/// </summary>
public sealed class ByteArrayMediaSource : IMediaSource
{
    private readonly byte[] data;
    private readonly long budget;
    private long charged;

    /// <param name="data">Bytes the source serves.</param>
    /// <param name="budget">Total bytes the source may serve before it fails like an exhausted host budget.</param>
    public ByteArrayMediaSource(byte[] data, long budget = 16 * 1024 * 1024)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.budget = budget;
    }

    /// <summary>Bytes charged against the budget so far.</summary>
    public long ChargedBytes => charged;

    /// <summary>Number of read calls, used to prove readers ask for the ranges they need rather than the file.</summary>
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
