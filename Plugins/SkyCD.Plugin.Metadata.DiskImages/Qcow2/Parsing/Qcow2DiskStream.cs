using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SkyCD.Plugin.Metadata.DiskImages.Qcow2.Exceptions;

namespace SkyCD.Plugin.Metadata.DiskImages.Qcow2.Parsing;

/// <summary>
/// A read-only view of one QCOW2 layer. Guest clusters resolve through the L1/L2 tables; unallocated
/// clusters fall through to the backing layer and deflate-compressed clusters are decompressed on demand.
/// </summary>
internal sealed class Qcow2DiskStream : Stream
{
    // Bits 9-55 of an L1/L2 entry hold an aligned host offset.
    private const ulong HostOffsetMask = 0x00FF_FFFF_FFFF_FE00UL;
    private const ulong CompressedClusterFlag = 1UL << 62;
    private const int L2CacheLimit = 64;

    private readonly Stream file;
    private readonly bool ownsFile;
    private readonly Qcow2Header header;
    private readonly Stream? backing;
    private readonly int clusterBits;
    private readonly int clusterSize;
    private readonly int l2Entries;
    private readonly int compressedOffsetBits;
    private readonly long fileLength;
    private readonly Dictionary<long, ulong[]> l2Cache = [];
    private byte[]? compressedCluster;
    private long compressedClusterIndex = -1;
    private long position;

    public Qcow2DiskStream(Stream file, bool ownsFile, Qcow2Header header, Stream? backing)
    {
        this.file = file;
        this.ownsFile = ownsFile;
        this.header = header;
        this.backing = backing;
        clusterBits = header.ClusterBits;
        clusterSize = 1 << clusterBits;
        l2Entries = clusterSize / 8;
        compressedOffsetBits = 70 - clusterBits;
        fileLength = file.Length;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => header.VirtualSize;

    public override long Position
    {
        get => position;
        set => position = ValidatePosition(value);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The buffer is too small for the requested range.");
        }

        var remaining = (int)Math.Min(count, header.VirtualSize - position);
        var written = 0;
        while (written < remaining)
        {
            var clusterIndex = position / clusterSize;
            var intra = (int)(position % clusterSize);
            var chunk = Math.Min(remaining - written, clusterSize - intra);
            var entry = GetL2Entry(clusterIndex);

            if (entry == 0)
            {
                ReadBacking(position, buffer, offset + written, chunk);
            }
            else if ((entry & CompressedClusterFlag) != 0)
            {
                Buffer.BlockCopy(GetCompressedCluster(entry, clusterIndex), intra, buffer, offset + written, chunk);
            }
            else if ((entry & 1UL) != 0 && header.Version >= 3)
            {
                Array.Clear(buffer, offset + written, chunk);
            }
            else
            {
                ReadHostCluster(entry, intra, buffer, offset + written, chunk);
            }

            position += chunk;
            written += chunk;
        }

        return written;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => header.VirtualSize + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        position = ValidatePosition(target);
        return position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            backing?.Dispose();
            if (ownsFile)
            {
                file.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static long ValidatePosition(long value) =>
        value < 0 ? throw new IOException("An attempt was made to move the position before the beginning of the stream.") : value;

    private ulong GetL2Entry(long clusterIndex)
    {
        var l1Index = clusterIndex / l2Entries;
        if (l1Index >= header.L1Size)
        {
            return 0;
        }

        var l1Entry = ReadHostUInt64(header.L1TableOffset + (l1Index * 8));
        var l2TableOffset = (long)(l1Entry & HostOffsetMask);
        if (l2TableOffset == 0)
        {
            return 0;
        }

        var table = GetL2Table(l2TableOffset);
        return table[clusterIndex % l2Entries];
    }

    private ulong[] GetL2Table(long offset)
    {
        if (l2Cache.TryGetValue(offset, out var table))
        {
            return table;
        }

        if (offset < 0 || offset > fileLength - clusterSize)
        {
            throw new Qcow2DamagedImageException();
        }

        var bytes = new byte[clusterSize];
        file.Position = offset;
        ReadExactly(file, bytes);

        table = new ulong[l2Entries];
        for (var index = 0; index < table.Length; index++)
        {
            table[index] = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(index * 8));
        }

        if (l2Cache.Count >= L2CacheLimit)
        {
            l2Cache.Clear();
        }

        l2Cache[offset] = table;
        return table;
    }

    private ulong ReadHostUInt64(long offset)
    {
        if (offset < 0 || offset > fileLength - 8)
        {
            throw new Qcow2DamagedImageException();
        }

        Span<byte> bytes = stackalloc byte[8];
        file.Position = offset;
        ReadExactly(file, bytes);
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    private void ReadHostCluster(ulong entry, int intra, byte[] buffer, int offset, int count)
    {
        var hostOffset = (long)(entry & HostOffsetMask);
        if (hostOffset <= 0 || hostOffset + intra > fileLength - count)
        {
            throw new Qcow2DamagedImageException();
        }

        file.Position = hostOffset + intra;
        ReadExactly(file, buffer.AsSpan(offset, count));
    }

    private void ReadBacking(long guestOffset, byte[] buffer, int offset, int count)
    {
        if (backing is null)
        {
            Array.Clear(buffer, offset, count);
            return;
        }

        backing.Position = guestOffset;
        var total = 0;
        while (total < count)
        {
            var read = backing.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                Array.Clear(buffer, offset + total, count - total);
                return;
            }

            total += read;
        }
    }

    private byte[] GetCompressedCluster(ulong entry, long clusterIndex)
    {
        if (clusterIndex == compressedClusterIndex && compressedCluster is not null)
        {
            return compressedCluster;
        }

        var hostOffset = (long)(entry & ((1UL << compressedOffsetBits) - 1));
        var sectors = (long)((entry >> compressedOffsetBits) & ((1UL << (62 - compressedOffsetBits)) - 1)) + 1;
        var available = (sectors * 512) - (hostOffset & 511);
        if (hostOffset <= 0 || hostOffset >= fileLength || available <= 0 || available > clusterSize + 512)
        {
            throw new Qcow2DamagedImageException();
        }

        available = Math.Min(available, fileLength - hostOffset);
        var compressed = new byte[available];
        file.Position = hostOffset;
        ReadExactly(file, compressed);

        var cluster = new byte[clusterSize];
        using var input = new MemoryStream(compressed, writable: false);
        using Stream decoder = HasZlibHeader(compressed)
            ? new ZLibStream(input, CompressionMode.Decompress)
            : new DeflateStream(input, CompressionMode.Decompress);

        var total = 0;
        while (total < cluster.Length)
        {
            var read = decoder.Read(cluster, total, cluster.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total != cluster.Length)
        {
            throw new Qcow2DamagedImageException();
        }

        compressedCluster = cluster;
        compressedClusterIndex = clusterIndex;
        return cluster;
    }

    private static bool HasZlibHeader(byte[] data)
    {
        if (data.Length < 2)
        {
            return false;
        }

        var header = (data[0] << 8) | data[1];
        return (data[0] & 0x0F) == 8 && header % 31 == 0;
    }

    private static void ReadExactly(Stream stream, byte[] buffer) =>
        ReadExactly(stream, buffer.AsSpan());

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new Qcow2DamagedImageException();
            }

            total += read;
        }
    }
}
