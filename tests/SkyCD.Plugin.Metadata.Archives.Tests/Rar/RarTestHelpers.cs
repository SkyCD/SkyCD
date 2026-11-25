using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Indexing;

namespace SkyCD.Plugin.Metadata.Archives.Rar.Tests;

/// <summary>Small test helpers shared by the RAR plugin tests.</summary>
internal static class RarTestHelpers
{
    /// <summary>Builds a work item request for an in-memory archive, optionally with a source path.</summary>
    public static IndexWorkItemRequest RequestFor(Stream stream, string fileName, string? sourcePath = null) =>
        RequestFor(stream, fileName, stream.CanSeek ? stream.Length : 0, sourcePath);

    /// <summary>Builds a work item request with an explicit size, required for forward-only sources.</summary>
    public static IndexWorkItemRequest RequestFor(Stream stream, string fileName, long sizeBytes,
        string? sourcePath = null) => new()
        {
            FileName = fileName,
            SourcePath = sourcePath,
            SizeBytes = sizeBytes,
            OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
        };

    /// <summary>Creates an empty temporary directory owned by the test.</summary>
    public static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"skycd-rar-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Lets the plugin dispose its source stream while the test keeps ownership of it.</summary>
    public sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A forward-only stream, like a network or pipe source.</summary>
    public sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream inner = new(content, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Cancels the token once the wrapped stream has produced <paramref name="bytesBeforeCancel"/> bytes.</summary>
    public sealed class CancellingStream(Stream inner, long bytesBeforeCancel, CancellationTokenSource cancellation)
        : Stream
    {
        private long served;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (served >= bytesBeforeCancel)
            {
                cancellation.Cancel();
            }

            var read = inner.Read(buffer, offset, count);
            served += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
