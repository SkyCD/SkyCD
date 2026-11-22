using System;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.EComic.Exceptions;

namespace SkyCD.Plugin.Metadata.EComic.Parsing;

/// <summary>
/// Seekable, read-only <see cref="Stream"/> over the host's bounded <see cref="IMediaSource"/>.
/// </summary>
/// <remarks>
/// Archive readers need random access and comic archives routinely exceed the indexing pass's source budget,
/// so the whole file cannot be buffered first. Reads are translated into exact source ranges and each read is
/// charged to the host's cumulative budget, which is what keeps a large archive inside the limits. The host
/// runs providers on worker threads without a synchronization context, so waiting for the source read
/// synchronously cannot deadlock; cancellation is checked on the way in so a cancelled indexing pass stops
/// even when the archive reader itself does not take a token.
/// </remarks>
internal sealed class MediaSourceStream : Stream
{
    private readonly IMediaSource source;
    private readonly CancellationToken cancellationToken;
    private long position;

    public MediaSourceStream(IMediaSource source, CancellationToken cancellationToken)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.cancellationToken = cancellationToken;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            if (source.Length > 0)
            {
                return source.Length;
            }

            throw new EComicArchiveException("the source length is unknown");
        }
    }

    public override long Position
    {
        get => position;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = Length;
        if (position >= length || buffer.IsEmpty)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, length - position);
        var memory = source.ReadAsync(position, count, cancellationToken).AsTask().GetAwaiter().GetResult();
        var read = Math.Min(memory.Length, count);
        memory.Span[..read].CopyTo(buffer);
        position += read;
        return read;
    }

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
            throw new EComicArchiveException("the archive reader seeked before the start of the source");
        }

        position = target;
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
