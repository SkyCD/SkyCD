using System;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Metadata.Executable.Exceptions;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>
/// Seekable, read-only <see cref="Stream"/> over a bounded <see cref="ExecutableSource"/>, for readers such as
/// <see cref="System.Reflection.PortableExecutable.PEHeaders"/> that require a stream. Reads stay within the source
/// limit and are charged to the host's budget; the reader only fetches the header blocks it actually parses, so
/// the image is never buffered wholesale.
/// </summary>
/// <remarks>
/// The host runs capabilities on worker threads without a synchronization context, so waiting for the source read
/// synchronously cannot deadlock. A windowed instance exposes an embedded filesystem, such as the ISO9660
/// volume an AppImage carries after its runtime, with stream position zero at the window's origin.
/// </remarks>
internal sealed class ExecutableSourceStream : Stream
{
    private readonly ExecutableSource source;
    private readonly CancellationToken cancellationToken;
    private readonly long origin;
    private readonly long length;
    private long position;

    public ExecutableSourceStream(ExecutableSource source, CancellationToken cancellationToken)
        : this(source, cancellationToken, 0, source?.Length ?? 0)
    {
    }

    public ExecutableSourceStream(ExecutableSource source, CancellationToken cancellationToken, long origin,
        long length)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        if (origin < 0 || length < 0 || origin > source.Length || length > source.Length - origin)
        {
            throw new ExecutableReadException("the stream window lies outside the readable image");
        }

        this.cancellationToken = cancellationToken;
        this.origin = origin;
        this.length = length;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position >= length || buffer.IsEmpty)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, length - position);
        var data = source.ReadAsync(origin + position, count, cancellationToken).GetAwaiter().GetResult();
        var read = Math.Min(data.Length, count);
        data.AsSpan(0, read).CopyTo(buffer);
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
            throw new ExecutableReadException("the reader seeked before the start of the source");
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
