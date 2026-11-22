using System;
using System.IO;
using SkyCD.Plugin.Metadata.Archives.Rar.Exceptions;

namespace SkyCD.Plugin.Metadata.Archives.Rar.Parsing;

/// <summary>
/// Wraps a RAR source stream so that data ending inside an entry that continues in another volume is reported
/// as a multi-volume failure instead of a silent end of archive, which the RAR reader would otherwise treat as
/// a clean end of data and ignore the truncation.
/// </summary>
/// <remarks>
/// The wrapper forwards data unchanged. Once the underlying stream ends it stops returning zero bytes - the
/// reader's end-of-data signal - and raises <see cref="MultiVolumeMissingException"/> on every further read
/// instead, so a truncated final entry fails loudly rather than being indexed as complete.
/// </remarks>
internal sealed class RarVolumeAwareStream : Stream
{
    private readonly Stream inner;
    private bool endReportedPending;

    private RarVolumeAwareStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    /// <summary>Wraps a stream that is read from its current position until the end of the archive.</summary>
    public static Stream Wrap(Stream source) =>
        source is RarVolumeAwareStream ? source : new RarVolumeAwareStream(source);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException("RAR sources are read forward-only.");

    public override long Position
    {
        get => throw new NotSupportedException("RAR sources are read forward-only.");
        set => throw new NotSupportedException("RAR sources are read forward-only.");
    }

    /// <inheritdoc/>
    public override bool CanTimeout => inner.CanTimeout;

    /// <inheritdoc/>
    public override int ReadTimeout
    {
        get => inner.ReadTimeout;
        set => inner.ReadTimeout = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (endReportedPending)
        {
            throw new MultiVolumeMissingException();
        }

        var read = inner.Read(buffer);
        if (read > 0)
        {
            return read;
        }

        endReportedPending = true;
        return 0;
    }

    public override int ReadByte()
    {
        Span<byte> single = stackalloc byte[1];
        return Read(single) == 0 ? -1 : single[0];
    }

    public override void Flush()
    {
    }

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
