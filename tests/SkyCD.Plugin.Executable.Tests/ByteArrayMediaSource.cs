using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Executable.Tests;

/// <summary>
/// In-memory <see cref="IMediaSource"/> that charges reads the way the host does, so the plugin is exercised
/// against the same bounded random-access contract it sees in production.
/// </summary>
internal sealed class ByteArrayMediaSource : IMediaSource
{
    private readonly byte[] data;
    private readonly long budget;
    private long charged;

    public ByteArrayMediaSource(byte[] data, long budget = 64 * 1024 * 1024)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.budget = budget;
    }

    public long ChargedBytes => charged;

    public int ReadCount { get; private set; }

    public long Length => data.Length;

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;

        if (offset < 0 || count < 0 || offset > Length || count > Length - offset)
        {
            throw new InvalidOperationException("The host rejected an out-of-range read.");
        }

        charged += count;
        if (charged > budget)
        {
            throw new InvalidOperationException("The host source budget was exceeded.");
        }

        return ValueTask.FromResult<ReadOnlyMemory<byte>>(data.AsMemory((int)offset, count));
    }
}
