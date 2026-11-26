using System;
using System.IO;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Read-only wrapper that fails once more than <c>limit</c> bytes have been delivered. Placed above a
/// decompression or archive stream, it bounds the bytes an untrusted container can expand to, independently
/// of the declared sizes it claims.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream inner;
    private readonly long limit;
    private readonly string resource;
    private long delivered;

    public BoundedReadStream(Stream inner, long limit, string resource)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        this.inner = inner;
        this.limit = limit;
        this.resource = resource;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => delivered;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        if (read > 0)
        {
            delivered += read;
            if (delivered > limit)
            {
                throw new VideoProjectLimitExceededException(resource, limit);
            }
        }

        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
