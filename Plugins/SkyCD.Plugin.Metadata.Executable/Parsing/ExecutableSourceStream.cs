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
/// synchronously cannot deadlock.
/// </remarks>
internal sealed class ExecutableSourceStream : Stream
{
    private readonly ExecutableSource source;
    private readonly CancellationToken cancellationToken;
    private long position;

    public ExecutableSourceStream(ExecutableSource source, CancellationToken cancellationToken)
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
        var length = source.Length;
        if (position >= length || buffer.IsEmpty)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, length - position);
        var data = source.ReadAsync(position, count, cancellationToken).GetAwaiter().GetResult();
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
            throw new ExecutableReadException("the PE reader seeked before the start of the source");
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
