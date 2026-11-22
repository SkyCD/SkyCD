using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.DiskImages.Nrg.Parsing;

/// <summary>
/// Read-only view of the 2048-byte user-data area of every sector in an NRG data track, so the
/// ISO 9660 reader can treat a 2336/2352-byte raw track as a plain 2048-byte-per-sector image.
/// </summary>
internal sealed class NrgDataTrackStream : Stream
{
    private readonly Stream source;
    private readonly bool leaveOpen;
    private readonly long trackStart;
    private readonly long sectorCount;
    private readonly int sectorSize;
    private readonly int dataOffsetInSector;
    private long position;

    public NrgDataTrackStream(Stream source, NrgDataTrack track, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(track);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The NRG source stream must be seekable.", nameof(source));
        }

        this.source = source;
        this.leaveOpen = leaveOpen;
        trackStart = track.StartOffset;
        sectorCount = track.SectorCount;
        sectorSize = track.SectorSize;
        dataOffsetInSector = track.DataOffsetInSector;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => sectorCount * NrgDataTrack.UserDataSize;

    public override long Position
    {
        get => position;
        set => position = value is >= 0 && value <= Length
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var total = 0;
        while (!buffer.IsEmpty && position < Length)
        {
            var sectorIndex = position / NrgDataTrack.UserDataSize;
            var offsetInSector = (int)(position % NrgDataTrack.UserDataSize);
            var available = NrgDataTrack.UserDataSize - offsetInSector;
            var toRead = Math.Min(buffer.Length, available);

            source.Position = trackStart + sectorIndex * sectorSize + dataOffsetInSector + offsetInSector;
            var read = source.Read(buffer[..toRead]);
            if (read <= 0)
            {
                break;
            }

            position += read;
            total += read;
            buffer = buffer[read..];
        }

        return total;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var total = 0;
        while (!buffer.IsEmpty && position < Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sectorIndex = position / NrgDataTrack.UserDataSize;
            var offsetInSector = (int)(position % NrgDataTrack.UserDataSize);
            var available = NrgDataTrack.UserDataSize - offsetInSector;
            var toRead = Math.Min(buffer.Length, available);

            source.Position = trackStart + sectorIndex * sectorSize + dataOffsetInSector + offsetInSector;
            var read = await source.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            position += read;
            total += read;
            buffer = buffer[read..];
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (position < 0 || position > Length)
        {
            throw new IOException("Attempted to seek outside the NRG data track.");
        }

        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
        {
            source.Dispose();
        }

        base.Dispose(disposing);
    }
}
