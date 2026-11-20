using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Daa.Exceptions;

namespace SkyCD.Plugin.Daa;

/// <summary>
/// Forward-only header facts read from a DAA volume.
/// </summary>
internal sealed record DaaHeader(
    uint Version,
    int ChunkSize,
    long IsoSize,
    long SizeOffset,
    long DataOffset);

/// <summary>
/// Seekable stream over the ISO image stored inside a (possibly multi-volume) DAA file.
/// Chunks are decompressed lazily and only one decompressed chunk is kept in memory.
/// </summary>
internal sealed class DaaImageStream : Stream
{
    private const int HeaderLength = 76;
    private const int MaxChunkSize = 64 * 1024 * 1024;
    private const int MaxChunkCount = 2_000_000;
    private const int MaxVolumes = 1_000;
    internal const int SupportedVersion = 0x100;

    private readonly DaaCompressedSource source;
    private readonly long isoSize;
    private readonly int chunkSize;
    private readonly long[] compressedOffsets;
    private readonly int[] compressedLengths;
    private readonly CancellationToken cancellationToken;
    private byte[]? cachedChunk;
    private int cachedChunkIndex = -1;
    private long position;
    private bool disposed;

    private DaaImageStream(
        DaaCompressedSource source,
        DaaHeader header,
        int[] compressedLengths,
        CancellationToken cancellationToken)
    {
        this.source = source;
        this.compressedLengths = compressedLengths;
        isoSize = header.IsoSize;
        chunkSize = header.ChunkSize;
        this.cancellationToken = cancellationToken;

        compressedOffsets = new long[compressedLengths.Length];
        long offset = 0;
        for (var index = 0; index < compressedLengths.Length; index++)
        {
            compressedOffsets[index] = offset;
            offset += compressedLengths[index];
        }
    }

    public override bool CanRead => !disposed;

    public override bool CanSeek => !disposed;

    public override bool CanWrite => false;

    public override long Length => isoSize;

    public override long Position
    {
        get => position;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            position = value;
        }
    }

    public static async Task<DaaImageStream> OpenAsync(
        Stream? source,
        string? sourcePath,
        bool ownsSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ownedStreams = new List<Stream>();
        var segments = new List<DaaCompressedSource.Segment>();
        string? temporaryDirectory = null;

        Stream first;
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            first = OpenVolume(sourcePath);
            ownedStreams.Add(first);
        }
        else
        {
            if (source is null)
            {
                throw new DaaDamagedImageException("no source stream is available");
            }

            first = source;
            if (!first.CanSeek)
            {
                var buffered = await BufferToTemporaryFileAsync(first, cancellationToken).ConfigureAwait(false);
                first = buffered.Stream;
                temporaryDirectory = buffered.Directory;
                ownedStreams.Add(first);
            }
            else if (ownsSource)
            {
                ownedStreams.Add(first);
            }
        }

        try
        {
            var header = ReadHeader(first, isContinuation: false);
            if (header.Version != SupportedVersion)
            {
                throw new DaaUnsupportedImageException($"version 0x{header.Version:X8}");
            }

            var chunkLengths = ReadChunkLengths(first, header);
            var firstLength = first.Length - header.DataOffset;
            if (firstLength <= 0)
            {
                throw new DaaDamagedImageException("the first volume contains no chunk data");
            }

            segments.Add(new DaaCompressedSource.Segment(first, header.DataOffset, firstLength));
            var required = SumLengths(chunkLengths);
            var available = firstLength;

            if (available < required)
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    throw new DaaDamagedImageException("chunk data requires companion volumes that are not available");
                }

                var naming = DaaVolumeNaming.Create(sourcePath) ??
                             throw new DaaDamagedImageException("chunk data is incomplete");
                for (var part = 1; available < required; part++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (part > MaxVolumes)
                    {
                        throw new DaaDamagedImageException("too many companion volumes");
                    }

                    var path = naming.GetPath(part);
                    if (!File.Exists(path))
                    {
                        throw new DaaMissingVolumeException(Path.GetFileName(path));
                    }

                    var partStream = OpenVolume(path);
                    try
                    {
                        var partHeader = ReadHeader(partStream, isContinuation: true);
                        if (partHeader.Version != SupportedVersion)
                        {
                            throw new DaaUnsupportedImageException($"version 0x{partHeader.Version:X8}");
                        }

                        var partLength = partStream.Length - partHeader.DataOffset;
                        if (partLength <= 0)
                        {
                            throw new DaaDamagedImageException(
                                $"volume '{Path.GetFileName(path)}' contains no chunk data");
                        }

                        segments.Add(new DaaCompressedSource.Segment(partStream, partHeader.DataOffset, partLength));
                        ownedStreams.Add(partStream);
                        available += partLength;
                    }
                    catch (Exception)
                    {
                        partStream.Dispose();
                        throw;
                    }
                }
            }

            if (available < required)
            {
                throw new DaaDamagedImageException("chunk data is truncated");
            }

            var compressedSource = new DaaCompressedSource(segments, ownedStreams, temporaryDirectory);
            return new DaaImageStream(compressedSource, header, chunkLengths, cancellationToken);
        }
        catch (Exception)
        {
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

            DeleteTemporaryDirectory(temporaryDirectory);
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.Length == 0 || position >= isoSize)
        {
            return 0;
        }

        var chunkIndex = (int)(position / chunkSize);
        var chunkOffset = (int)(position % chunkSize);
        EnsureChunk(chunkIndex);

        var available = cachedChunk!.Length - chunkOffset;
        var count = Math.Min(buffer.Length, available);
        cachedChunk.AsSpan(chunkOffset, count).CopyTo(buffer);
        position += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => isoSize + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            throw new IOException("Cannot seek before the start of the DAA image.");
        }

        position = target;
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            cachedChunk = null;
            source.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureChunk(int index)
    {
        if (index == cachedChunkIndex)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var expectedLength = ExpectedChunkLength(index);
        var compressedLength = compressedLengths[index];
        var compressed = ArrayPool<byte>.Shared.Rent(compressedLength);
        try
        {
            source.ReadExactly(compressedOffsets[index], compressed.AsSpan(0, compressedLength), cancellationToken);

            var output = new byte[expectedLength];
            using var input = new MemoryStream(compressed, 0, compressedLength, writable: false);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            var read = 0;
            while (read < expectedLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = inflater.Read(output, read, expectedLength - read);
                if (current == 0)
                {
                    throw new DaaDamagedImageException($"chunk {index} ended before its expected size");
                }

                read += current;
            }

            if (inflater.ReadByte() != -1)
            {
                throw new DaaDamagedImageException($"chunk {index} expands beyond its expected size");
            }

            cachedChunk = output;
            cachedChunkIndex = index;
        }
        catch (InvalidDataException)
        {
            throw new DaaDamagedImageException($"chunk {index} is not valid DEFLATE data");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
        }
    }

    private int ExpectedChunkLength(int index)
    {
        var start = (long)index * chunkSize;
        return (int)Math.Min(chunkSize, isoSize - start);
    }

    private static DaaHeader ReadHeader(Stream stream, bool isContinuation)
    {
        if (stream.Length < HeaderLength)
        {
            throw new DaaDamagedImageException("the header is truncated");
        }

        var header = new byte[HeaderLength];
        stream.Position = 0;
        ReadExactly(stream, header);

        var signature = Encoding.ASCII.GetString(header, 0, 16);
        if (isContinuation)
        {
            if (!signature.StartsWith("DAA VOL", StringComparison.Ordinal))
            {
                throw new DaaDamagedImageException("a companion volume has an invalid signature");
            }
        }
        else
        {
            if (!signature.StartsWith("DAA", StringComparison.Ordinal) ||
                !IsEmpty(header.AsSpan(3, 13)))
            {
                throw new DaaDamagedImageException("the DAA signature is missing");
            }
        }

        var sizeOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        var version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
        var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24));
        var mustBeOne = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28));
        var mustBeZero = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(32));
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(36));
        var isoSize = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(40));

        if (mustBeOne != 1 || mustBeZero != 0)
        {
            throw new DaaDamagedImageException("the header flags are invalid");
        }

        if (sizeOffset < HeaderLength || sizeOffset > stream.Length || dataOffset < sizeOffset ||
            dataOffset > stream.Length)
        {
            throw new DaaDamagedImageException("the header offsets are invalid");
        }

        if (chunkSize == 0 || chunkSize > MaxChunkSize)
        {
            throw new DaaDamagedImageException("the declared chunk size is invalid");
        }

        if (isoSize == 0)
        {
            throw new DaaDamagedImageException("the declared ISO size is invalid");
        }

        if (!isContinuation && isoSize > (ulong)long.MaxValue)
        {
            throw new DaaDamagedImageException("the declared ISO size is too large");
        }

        return new DaaHeader(version, (int)chunkSize, (long)isoSize, sizeOffset, dataOffset);
    }

    private static int[] ReadChunkLengths(Stream stream, DaaHeader header)
    {
        ReadExtensionRecords(stream, header);

        var tableLength = header.DataOffset - header.SizeOffset;
        if (tableLength <= 0 || tableLength % 3 != 0)
        {
            throw new DaaDamagedImageException("the chunk length table is invalid");
        }

        var count = tableLength / 3;
        var expectedCount = (header.IsoSize + header.ChunkSize - 1) / header.ChunkSize;
        if (count != expectedCount || count <= 0 || count > MaxChunkCount)
        {
            throw new DaaDamagedImageException("the chunk length table does not match the ISO size");
        }

        var table = new byte[checked((int)tableLength)];
        stream.Position = header.SizeOffset;
        ReadExactly(stream, table);

        var lengths = new int[count];
        for (var index = 0; index < count; index++)
        {
            var offset = index * 3;
            var length = (table[offset] << 16) | table[offset + 1] | (table[offset + 2] << 8);
            if (length <= 0)
            {
                throw new DaaDamagedImageException($"chunk {index} has an invalid compressed length");
            }

            lengths[index] = length;
        }

        return lengths;
    }

    private static void ReadExtensionRecords(Stream stream, DaaHeader header)
    {
        long position = HeaderLength;
        while (position < header.SizeOffset)
        {
            if (header.SizeOffset - position < 8)
            {
                throw new DaaDamagedImageException("a header extension record is truncated");
            }

            var record = new byte[8];
            stream.Position = position;
            ReadExactly(stream, record);
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4));
            if (length < 8 || position + length > header.SizeOffset)
            {
                throw new DaaDamagedImageException("a header extension record has an invalid length");
            }

            if (type == 3)
            {
                throw new DaaUnsupportedImageException("encrypted images (password protection)");
            }

            position += (long)length;
        }
    }

    private static long SumLengths(int[] lengths)
    {
        long total = 0;
        foreach (var length in lengths)
        {
            total += length;
        }

        return total;
    }

    private static bool IsEmpty(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var current = stream.Read(destination[read..]);
            if (current == 0)
            {
                throw new DaaDamagedImageException("the file ended unexpectedly");
            }

            read += current;
        }
    }

    private static Stream OpenVolume(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.RandomAccess);

    private static async Task<(Stream Stream, string Directory)> BufferToTemporaryFileAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-daa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "image.daa");
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81_920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            return (OpenVolume(path), directory);
        }
        catch (Exception)
        {
            DeleteTemporaryDirectory(directory);
            throw;
        }
    }

    private static void DeleteTemporaryDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
    }
}
