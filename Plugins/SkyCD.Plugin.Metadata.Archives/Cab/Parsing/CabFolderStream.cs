using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using SkyCD.Plugin.Metadata.Archives.Cab.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Cab.Models;

namespace SkyCD.Plugin.Metadata.Archives.Cab.Parsing;

/// <summary>
/// Reads the contiguous CFDATA blocks of a single folder and exposes the uncompressed folder stream.
/// Supports stored (type 0) and MSZIP (type 1) folders. Other compression types raise
/// <see cref="CabUnsupportedEntryException"/> when reading starts, while keeping the entry listable.
/// </summary>
internal sealed class CabFolderStream : Stream
{
    /// <summary>Gets the maximum uncompressed size of one MSZIP block.</summary>
    public const int MsZipBlockBytes = 32_768;

    private const int BlockHeaderBytes = 8;
    private const int MaxBlockBytes = 0xFFFF;

    private readonly Stream source;
    private readonly CabFolder folder;
    private readonly int reserveBytes;
    private readonly byte[] inputBuffer = new byte[MsZipBlockBytes];
    private readonly byte[] outputBuffer = new byte[MsZipBlockBytes];

    private long sourcePosition;
    private int remainingBlocks;
    private int outputOffset;
    private int outputCount;
    private bool finished;

    /// <summary>
    /// Creates a folder reader positioned at <paramref name="startOffset"/>, the first CFDATA block.
    /// </summary>
    public CabFolderStream(Stream source, CabFolder folder, long startOffset, int reserveBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(folder);
        this.source = source;
        this.folder = folder;
        this.reserveBytes = Math.Max(reserveBytes, 0);
        sourcePosition = startOffset;
        remainingBlocks = folder.DataBlockCount;
    }

    /// <summary>Gets a value indicating whether the folder compression type has a decoder.</summary>
    public bool IsSupported => folder.CompressionType is CabArchive.TypeStored or CabArchive.TypeMsZip;

    /// <summary>Gets the folder compression type as declared in the cabinet header.</summary>
    public int CompressionType => folder.CompressionType;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Skips <paramref name="count"/> uncompressed folder bytes. Returns the number actually skipped; a
    /// short result means the folder ended earlier than the member offset claimed.
    /// </summary>
    public long Skip(long count, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return 0;
        }

        long skipped = 0;
        while (skipped < count)
        {
            var read = Read(outputBuffer, 0, (int)Math.Min(outputBuffer.Length, count - skipped), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            skipped += read;
        }

        return skipped;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer, offset, count, CancellationToken.None);

    /// <summary>Reads up to <paramref name="count"/> uncompressed folder bytes into <paramref name="buffer"/>.</summary>
    public int Read(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The buffer is too small for the requested range.", nameof(count));
        }

        if (!IsSupported)
        {
            throw CabUnsupported();
        }

        if (count == 0)
        {
            return 0;
        }

        if (outputOffset >= outputCount && !Fill(cancellationToken))
        {
            return 0;
        }

        var take = Math.Min(outputCount - outputOffset, count);
        Buffer.BlockCopy(outputBuffer, outputOffset, buffer, offset, take);
        outputOffset += take;
        return take;
    }

    private bool Fill(CancellationToken cancellationToken)
    {
        outputOffset = 0;
        outputCount = 0;
        if (finished || remainingBlocks <= 0)
        {
            finished = true;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        source.Position = sourcePosition;
        var payload = ReadPayloadLength();
        remainingBlocks--;

        if (folder.CompressionType == CabArchive.TypeStored)
        {
            ReadPayload(payload);
            outputCount = payload;
        }
        else
        {
            outputCount = InflateMsZipBlock(payload, cancellationToken);
        }

        return outputCount > 0;
    }

    private void ReadPayload(int payload)
    {
        if (payload > outputBuffer.Length)
        {
            throw new CabDamagedException();
        }

        var read = ReadFromSource(outputBuffer.AsSpan(0, payload));
        if (read != payload)
        {
            throw new CabDamagedException();
        }
    }

    private int InflateMsZipBlock(int payload, CancellationToken cancellationToken)
    {
        if (payload > inputBuffer.Length)
        {
            throw new CabDamagedException();
        }

        if (ReadFromSource(inputBuffer.AsSpan(0, payload)) != payload)
        {
            throw new CabDamagedException();
        }

        // Each MSZIP block is a raw deflate stream of at most 32 KiB, optionally preceded by the "CK" marker.
        var start = payload >= 2 && inputBuffer[0] == (byte)'C' && inputBuffer[1] == (byte)'K' ? 2 : 0;
        try
        {
            using var inflater = new DeflateStream(new MemoryStream(inputBuffer, start, payload - start, writable: false),
                CompressionMode.Decompress);
            var produced = 0;
            while (produced < outputBuffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var got = inflater.Read(outputBuffer, produced, outputBuffer.Length - produced);
                if (got <= 0)
                {
                    break;
                }

                produced += got;
            }

            return produced;
        }
        catch (InvalidDataException)
        {
            throw new CabDamagedException();
        }
    }

    /// <summary>
    /// Reads the CFDATA header at the current source position and advances it past the block, including
    /// the optional per-block reserve area. Returns the number of payload bytes the block contributes to
    /// the folder stream.
    /// </summary>
    private int ReadPayloadLength()
    {
        var header = new byte[BlockHeaderBytes];
        if (ReadFromSource(header) != header.Length)
        {
            throw new CabDamagedException();
        }

        var cbData = header[4] | (header[5] << 8);
        var cbUncomp = header[6] | (header[7] << 8);
        var reserve = Math.Max(reserveBytes, 0);
        if (cbData < BlockHeaderBytes + reserve || cbData > MaxBlockBytes)
        {
            throw new CabDamagedException();
        }

        // When CFHEADER declares a data reserve, each CFDATA frame carries that many bytes in front of
        // its payload; they are not part of the folder stream.
        if (reserve > 0)
        {
            var reserved = new byte[reserve];
            if (ReadFromSource(reserved) != reserved.Length)
            {
                throw new CabDamagedException();
            }
        }

        sourcePosition += cbData;
        return cbData - BlockHeaderBytes - reserve;
    }

    private int ReadFromSource(Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = source.Read(destination[total..]);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private CabUnsupportedEntryException CabUnsupported() => new(folder.FirstEntryName, folder.CompressionType);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
