using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.YamlToml.Tests;

internal sealed class ByteArrayMediaSource : IMediaSource
{
    private readonly byte[] data;

    public ByteArrayMediaSource(byte[] data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public int ReadCount { get; private set; }

    public long Length => data.Length;

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        if (offset < 0 || count < 0 || offset > Length || count > Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return ValueTask.FromResult<ReadOnlyMemory<byte>>(new ReadOnlyMemory<byte>(data, (int)offset, count));
    }
}
