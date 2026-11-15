using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Read-only seekable <see cref="Stream"/> over the host's bounded <see cref="IMediaSource"/>.
/// </summary>
/// <remarks>
/// Decoders and metadata readers expect a stream, but the host only grants bounded random-access reads and
/// charges every byte against a cumulative budget. Each fetched byte is therefore retained: re-reading or
/// seeking backwards is free, and the total charge equals the high-water mark rather than the number of
/// calls. Reads are fetched in blocks, so a metadata reader scanning byte by byte does not turn into
/// thousands of host calls. Going past the host budget surfaces the host's own limit exception, which is
/// how oversized sources are rejected.
/// </remarks>
internal sealed class MediaSourceStream : Stream
{
    private const int FetchBlockSize = 64 * 1024;

    private readonly IMediaSource source;
    private readonly CancellationToken cancellationToken;
    private readonly MemoryStream cache = new();
    private long position;
    private bool isDisposed;

    public MediaSourceStream(IMediaSource source, CancellationToken cancellationToken)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.cancellationToken = cancellationToken;
    }

    public override bool CanRead => !isDisposed;

    public override bool CanSeek => !isDisposed;

    public override bool CanWrite => false;

    public override long Length => source.Length;

    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("The buffer is too small for the requested range.", nameof(buffer));
        }

        return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask()
            .GetAwaiter().GetResult();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        ReadAsync(new Memory<byte>(buffer, offset, count), token).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();

        if (buffer.Length == 0 || position >= source.Length)
        {
            return 0;
        }

        var wanted = (int)Math.Min(buffer.Length, source.Length - position);
        await EnsureCachedAsync(position + wanted, linked.Token).ConfigureAwait(false);

        if (!cache.TryGetBuffer(out var cached))
        {
            throw new InvalidOperationException("The source cache is not readable.");
        }

        new ReadOnlySpan<byte>(cached.Array, cached.Offset + (int)position, wanted).CopyTo(buffer.Span);
        position += wanted;
        return wanted;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => source.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin.")
        };

        if (target < 0)
        {
            throw new IOException("Cannot seek before the start of the source.");
        }

        // Seeking past the end is allowed; the next read simply reports end of stream.
        position = target;
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            isDisposed = true;
            if (disposing)
            {
                cache.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private async ValueTask EnsureCachedAsync(long target, CancellationToken token)
    {
        if (cache.Length >= target)
        {
            return;
        }

        var start = cache.Length;
        var remaining = source.Length - start;
        if (remaining <= 0)
        {
            return;
        }

        var block = (int)Math.Min(Math.Max(target - start, FetchBlockSize), remaining);
        var bytes = await source.ReadAsync(start, block, token).ConfigureAwait(false);
        if (bytes.Length != block)
        {
            throw new EndOfStreamException("The source returned fewer bytes than requested.");
        }

        cache.Seek(0, SeekOrigin.End);
        cache.Write(bytes.Span);
    }
}
