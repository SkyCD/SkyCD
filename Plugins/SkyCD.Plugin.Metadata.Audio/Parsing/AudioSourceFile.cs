using System;
using System.IO;
using System.Threading;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Presents the host's bounded source to TagLibSharp, whose API is synchronous and position-based.
/// </summary>
/// <remarks>
/// TagLib asks for the ranges it needs — tag blocks at the head, the last page of an Ogg stream, an atom tree
/// in an MP4 — so wrapping the source instead of copying the file keeps large sources within the host's read
/// budget. The file name and MIME type passed to <see cref="TagLib.File.Create(TagLib.File.IFileAbstraction, string, TagLib.ReadStyle)"/>
/// decide which container reader runs; the stream only supplies bytes.
/// </remarks>
internal sealed class AudioSourceFile(AudioSourceReader reader, string name, CancellationToken cancellationToken)
    : TagLib.File.IFileAbstraction
{
    public string Name { get; } = name;

    public Stream ReadStream => new SourceStream(reader, cancellationToken);

    /// <summary>Only read access is ever requested; writing would mutate the catalogued file.</summary>
    public Stream WriteStream => throw new NotSupportedException("The audio source is read-only.");

    public void CloseStream(Stream stream) => stream.Dispose();

    private sealed class SourceStream(AudioSourceReader reader, CancellationToken cancellationToken) : Stream
    {
        private long position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => reader.Length;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var total = 0;
            while (total < buffer.Length && position < reader.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // TagLib is synchronous, so the host's asynchronous range read is completed here; this runs on
                // the plugin's background thread, which has no synchronization context to capture.
                var chunk = reader
                    .ReadAsync(position, buffer.Length - total, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                if (chunk.IsEmpty)
                {
                    break;
                }

                chunk.Span.CopyTo(buffer[total..]);
                total += chunk.Length;
                position += chunk.Length;
            }

            return total;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => reader.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (target < 0)
            {
                throw new IOException("Cannot seek before the start of the audio source.");
            }

            position = target;
            return position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException("The audio source is read-only.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("The audio source is read-only.");
    }
}
