using System;
using System.IO;
using System.Threading;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image.Parsing.Paint;

/// <summary>Lets ZipArchive fetch its directory and selected entries without buffering the source.</summary>
internal sealed class ArchiveSourceStream(IMediaSource source, long length, CancellationToken token) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        token.ThrowIfCancellationRequested();
        var count = (int)Math.Min(Math.Min(buffer.Length, SourcePrefixChunkBytes), Math.Max(0, length - Position));
        if (count == 0)
        {
            return 0;
        }
        if (count > source.RemainingReadBytes)
        {
            throw new EndOfStreamException();
        }

        // ZipArchive's directory reader is synchronous. The host runs each capability on a worker.
        var read = source.ReadAsync(Position, count, token);
        var bytes = read.IsCompletedSuccessfully ? read.Result : read.AsTask().GetAwaiter().GetResult();
        bytes.Span.CopyTo(buffer);
        Position += bytes.Length;
        return bytes.Length;
    }

    private const int SourcePrefixChunkBytes = 256 * 1024;

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target < 0)
        {
            throw new IOException("Cannot seek before the start of the archive.");
        }
        return Position = target;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
