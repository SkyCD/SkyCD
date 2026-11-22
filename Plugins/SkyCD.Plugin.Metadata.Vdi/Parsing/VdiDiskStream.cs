using System;
using System.Buffers.Binary;
using System.IO;
using SkyCD.Plugin.Metadata.Vdi.Exceptions;
using SkyCD.Plugin.Metadata.Vdi.Models;

namespace SkyCD.Plugin.Metadata.Vdi.Parsing;

/// <summary>
/// A read-only view of the guest disk stored in a VirtualBox VDI image. Fixed images are mapped to a
/// constant offset and dynamic images through their block map, which is loaded lazily and cached so a
/// damaged block entry only affects the reads that touch it.
/// </summary>
internal sealed class VdiDiskStream : Stream
{
    private const int MaxCachedBlockMaps = 4;

    private readonly Stream source;
    private readonly bool ownsSource;
    private readonly VdiHeader header;
    private readonly long fixedDataOffset;
    private readonly long[]?[] blockMaps;
    private readonly long[] blockMapLoadStamp;
    private long stamp;
    private long position;
    private bool disposed;

    public VdiDiskStream(Stream source, bool ownsSource, VdiHeader header)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(header);
        this.source = source;
        this.ownsSource = ownsSource;
        this.header = header;
        fixedDataOffset = header.IsFixed ? VdiHeader.FixedDataOffset : 0;

        var blocksPerMap = header.SectorSize * 8;
        var mapCount = header.IsFixed ? 0 : (int)((header.BlockCount + blocksPerMap - 1) / blocksPerMap);
        blockMaps = new long[mapCount][];
        blockMapLoadStamp = new long[mapCount];
    }

    /// <summary>Gets the VirtualBox header of this image.</summary>
    public VdiHeader Header => header;

    public override bool CanRead => !disposed;

    public override bool CanSeek => !disposed;

    public override bool CanWrite => false;

    public override long Length => header.DiskSize;

    public override long Position
    {
        get => position;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            position = value;
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (position >= header.DiskSize || buffer.Length == 0)
        {
            return 0;
        }

        var available = header.DiskSize - position;
        var requested = (int)Math.Min(buffer.Length, available);
        var written = 0;

        while (written < requested)
        {
            var guestOffset = position + written;
            var sectorSize = header.SectorSize;
            var sector = guestOffset / sectorSize;
            var offsetInSector = (int)(guestOffset % sectorSize);
            var sectorRemaining = Math.Min(requested - written, sectorSize - offsetInSector);

            var mapped = TryMapSector(sector);
            if (!mapped.HasValue)
            {
                buffer.Slice(written, sectorRemaining).Clear();
            }
            else
            {
                ReadExactlyAt(mapped.Value + offsetInSector, buffer.Slice(written, sectorRemaining));
            }

            written += sectorRemaining;
        }

        position += written;
        return written;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => header.DiskSize + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            throw new IOException("Attempted to seek before the start of the VDI disk.");
        }

        position = target;
        return position;
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("VDI images are opened read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("VDI images are opened read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            if (ownsSource)
            {
                try
                {
                    source.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort release of a damaged image stream.
                }
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Maps one guest sector to its byte offset in the image. Returns <c>null</c> for an unallocated
    /// block, whose contents read back as zeros.
    /// </summary>
    private long? TryMapSector(long sector)
    {
        if (header.IsFixed)
        {
            return fixedDataOffset + sector * header.SectorSize;
        }

        var sectorsPerBlock = header.BlockSize / header.SectorSize;
        var block = sector / sectorsPerBlock;
        var sectorInBlock = sector % sectorsPerBlock;
        var blockMap = GetBlockMap(block / (header.SectorSize * 8));
        var entry = blockMap[block % (header.SectorSize * 8)];
        if (entry == VdiBlock.Unallocated)
        {
            return null;
        }

        var dataOffset = header.DataOffset + entry * header.BlockSize + sectorInBlock * header.SectorSize;
        if (dataOffset < header.DataOffset || dataOffset + header.SectorSize > source.Length)
        {
            throw new VdiDamagedImageException();
        }

        return dataOffset;
    }

    /// <summary>Loads and caches a block map, a page of block-map entries that fits in one sector.</summary>
    private long[] GetBlockMap(long mapIndex)
    {
        if (mapIndex < 0 || mapIndex >= blockMaps.Length)
        {
            throw new VdiDamagedImageException();
        }

        var cached = blockMaps[mapIndex];
        if (cached is not null)
        {
            blockMapLoadStamp[mapIndex] = ++stamp;
            return cached;
        }

        var entriesPerMap = header.SectorSize * 8;
        var firstBlock = mapIndex * entriesPerMap;
        var remaining = header.BlockCount - firstBlock;
        var entryCount = (int)Math.Min(entriesPerMap, remaining);
        var bytes = new byte[entryCount * 4];
        ReadExactlyAt((long)header.OffsetsOffset + firstBlock * 4, bytes);

        var values = new long[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4, 4));
        }

        EvictOldestBlockMap();
        blockMaps[mapIndex] = values;
        blockMapLoadStamp[mapIndex] = ++stamp;
        return values;
    }

    private void EvictOldestBlockMap()
    {
        var loaded = 0;
        var oldest = -1;
        for (var index = 0; index < blockMaps.Length; index++)
        {
            if (blockMaps[index] is null)
            {
                continue;
            }

            loaded++;
            if (oldest < 0 || blockMapLoadStamp[index] < blockMapLoadStamp[oldest])
            {
                oldest = index;
            }
        }

        if (loaded >= MaxCachedBlockMaps && oldest >= 0)
        {
            blockMaps[oldest] = null;
        }
    }

    private void ReadExactlyAt(long offset, Span<byte> destination)
    {
        source.Position = offset;
        var total = 0;
        while (total < destination.Length)
        {
            var read = source.Read(destination[total..]);
            if (read == 0)
            {
                throw new VdiDamagedImageException();
            }

            total += read;
        }
    }
}
