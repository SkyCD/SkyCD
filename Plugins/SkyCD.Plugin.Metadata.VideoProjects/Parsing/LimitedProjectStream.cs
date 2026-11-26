using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal sealed class LimitedProjectStream(Stream inner, long limit) : Stream
{
    private long bytesRead;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, limit - bytesRead + 1)]);
        bytesRead += read;
        if (bytesRead > limit)
            throw new ProjectLimitException("decoded XML bytes");
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
