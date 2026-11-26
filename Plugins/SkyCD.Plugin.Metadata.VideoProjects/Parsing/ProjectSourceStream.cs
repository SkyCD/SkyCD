using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

// Keep recently read blocks to avoid charging the host again for signatures and ZIP directory seeks.
internal sealed class ProjectSourceStream(IMediaSource source, CancellationToken token) : Stream
{
    private const int BlockSize = 8192;
    private readonly Dictionary<long, ReadOnlyMemory<byte>> blocks = [];
    private readonly Queue<long> blockOrder = [];
    private long position;

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
        token.ThrowIfCancellationRequested();
        if (buffer.IsEmpty || Position >= Length)
            return 0;

        var start = Position / BlockSize * BlockSize;
        if (!blocks.TryGetValue(start, out var block))
        {
            var count = (int)Math.Min(BlockSize, Length - start);
            if (count > source.RemainingReadBytes)
                throw new ProjectLimitException("source read budget");

            var read = source.ReadAsync(start, count, token);
            block = read.IsCompletedSuccessfully ? read.Result : read.AsTask().GetAwaiter().GetResult();
            if (block.Length != count)
                throw new EndOfStreamException();

            if (blocks.Count == 128)
                blocks.Remove(blockOrder.Dequeue());
            blocks.Add(start, block);
            blockOrder.Enqueue(start);
        }

        var index = (int)(Position - start);
        var copied = Math.Min(buffer.Length, block.Length - index);
        block.Span.Slice(index, copied).CopyTo(buffer);
        Position += copied;
        return copied;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return Position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
