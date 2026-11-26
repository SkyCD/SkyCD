using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Read-only seekable view over <see cref="IMediaSource"/>. The host exposes bounded random access only, so
/// container readers that need a <see cref="Stream"/> get this adapter instead of a filesystem path. Reads keep
/// charging the host's cumulative byte budget; an exhausted budget surfaces as the usual exception.
/// </summary>
internal sealed class MediaSourceStream : Stream
{
    private readonly IMediaSource source;
    private readonly CancellationToken cancellationToken;
    private long position;

    internal MediaSourceStream(IMediaSource source, CancellationToken cancellationToken)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.cancellationToken = cancellationToken;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => source.Length;

    public override long Position
    {
        get => position;
        set => position = value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => ReadCore(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ReadCore(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            throw new IOException("Cannot seek before the start of the source.");
        }

        position = target;
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int ReadCore(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var remaining = Length - position;
        if (remaining <= 0)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, remaining);
        var memory = source.ReadAsync(position, count, cancellationToken).AsTask().GetAwaiter().GetResult();
        memory.Span.CopyTo(buffer);
        position += memory.Length;
        return memory.Length;
    }
}
