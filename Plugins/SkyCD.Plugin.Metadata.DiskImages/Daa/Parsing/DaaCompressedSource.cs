using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Metadata.DiskImages.Daa.Exceptions;
using SkyCD.Plugin.Storage;

namespace SkyCD.Plugin.Metadata.DiskImages.Daa.Parsing;

/// <summary>
/// Reads a logical compressed byte range from one or more DAA volume data segments.
/// </summary>
internal sealed class DaaCompressedSource : IDisposable
{
    private readonly List<Segment> segments;
    private readonly List<Stream> ownedStreams;
    private readonly ArchiveExtractionWorkspace? temporarySource;
    private bool disposed;

    public DaaCompressedSource(List<Segment> segments, List<Stream> ownedStreams, ArchiveExtractionWorkspace? temporarySource)
    {
        this.segments = segments;
        this.ownedStreams = ownedStreams;
        this.temporarySource = temporarySource;
        Length = 0;
        foreach (var segment in segments)
        {
            Length += segment.Length;
        }
    }

    public long Length { get; }

    public void ReadExactly(long offset, Span<byte> destination, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (offset < 0 || destination.Length < 0 || offset + destination.Length > Length)
        {
            throw new DaaDamagedImageException("chunk data is truncated");
        }

        var written = 0;
        var position = offset;
        foreach (var segment in segments)
        {
            if (written == destination.Length)
            {
                break;
            }

            if (position >= segment.Length)
            {
                position -= segment.Length;
                continue;
            }

            var take = (int)Math.Min(segment.Length - position, destination.Length - written);
            segment.Stream.Position = segment.Start + position;
            ReadExactlyFromStream(segment.Stream, destination.Slice(written, take), cancellationToken);
            written += take;
            position = 0;
        }

        if (written != destination.Length)
        {
            throw new DaaDamagedImageException("chunk data is truncated");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var stream in ownedStreams)
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }

        // The buffered source is removed only after every stream reading it is closed.
        temporarySource?.Dispose();
    }

    private static void ReadExactlyFromStream(Stream stream, Span<byte> destination, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < destination.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stream.Read(destination[read..]);
            if (current == 0)
            {
                throw new DaaDamagedImageException("chunk data is incomplete");
            }

            read += current;
        }
    }

    internal sealed record Segment(Stream Stream, long Start, long Length);
}
