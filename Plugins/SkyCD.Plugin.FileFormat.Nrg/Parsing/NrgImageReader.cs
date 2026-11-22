using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.Nrg.Exceptions;

namespace SkyCD.Plugin.FileFormat.Nrg.Parsing;

/// <summary>
/// Reads the Nero footer and chunk chain of an NRG image and resolves the byte layout of the first
/// data track. All NRG integers are stored big endian; version 1 uses 32-bit offsets and version 2
/// uses 64-bit offsets.
/// </summary>
internal static class NrgImageReader
{
    private const int MinimumFooterSize = 8;
    private const int MaxTrackChunkPayload = 1_048_576;
    private static ReadOnlySpan<byte> NeroV1Footer => "NERO"u8;
    private static ReadOnlySpan<byte> NeroV2Footer => "NER5"u8;

    /// <summary>Resolves the data track, or throws <see cref="NrgImageFormatException"/> when unsupported.</summary>
    public static NrgDataTrack ReadDataTrack(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new NrgImageFormatException("the source stream is not seekable.");
        }

        source.Position = 0;
        var length = source.Length;
        if (length < MinimumFooterSize + 8)
        {
            throw new NrgImageFormatException("the file is too small to contain an NRG image.");
        }

        var (firstChunkOffset, footerSize) = ReadFooter(source, length);
        var footerStart = length - footerSize;
        if (firstChunkOffset < 8 || firstChunkOffset > footerStart - 8)
        {
            throw new NrgImageFormatException("the chunk chain offset is outside the image.");
        }

        var candidates = new List<TrackCandidate>();
        var position = firstChunkOffset;
        Span<byte> header = stackalloc byte[8];
        while (position + 8 <= footerStart)
        {
            source.Position = position;
            ReadExactly(source, header);

            var chunkId = Encoding.ASCII.GetString(header[..4]);
            var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            if (chunkSize < 8 || position + chunkSize > footerStart)
            {
                throw new NrgImageFormatException("a chunk extends past the image footer.");
            }

            var payloadOffset = position + 8;
            var payloadSize = (int)chunkSize - 8;
            switch (chunkId)
            {
                case "DAOX":
                    ReadDaox(source, payloadOffset, payloadSize, firstChunkOffset, candidates);
                    break;
                case "DAOI":
                    ReadDaoi(source, payloadOffset, payloadSize, firstChunkOffset, candidates);
                    break;
                case "ETN2":
                    ReadEtn2(source, payloadOffset, payloadSize, firstChunkOffset, candidates);
                    break;
                case "ETNF":
                    ReadEtnf(source, payloadOffset, payloadSize, firstChunkOffset, candidates);
                    break;
                case "END!":
                    return SelectTrack(candidates);
            }

            position += chunkSize;
        }

        return SelectTrack(candidates);
    }

    private static (long Offset, int FooterSize) ReadFooter(Stream source, long length)
    {
        Span<byte> footer = stackalloc byte[12];
        source.Position = length - footer.Length;
        ReadExactly(source, footer);

        if (footer[..4].SequenceEqual(NeroV2Footer))
        {
            return ((long)BinaryPrimitives.ReadUInt64BigEndian(footer[4..]), 12);
        }

        source.Position = length - MinimumFooterSize;
        ReadExactly(source, footer[..MinimumFooterSize]);
        if (footer[..4].SequenceEqual(NeroV1Footer))
        {
            return (BinaryPrimitives.ReadUInt32BigEndian(footer[4..MinimumFooterSize]), 8);
        }

        throw new NrgImageFormatException("the Nero footer was not found.");
    }

    private static void ReadDaox(Stream source, long payloadOffset, int payloadSize, long dataBoundary,
        List<TrackCandidate> candidates)
    {
        const int headerSize = 22;
        const int trackSize = 42;
        if (payloadSize < headerSize + trackSize)
        {
            return;
        }

        var payload = ReadPayload(source, payloadOffset, payloadSize);
        if (payload is null)
        {
            return;
        }

        var firstTrack = payload[20];
        var lastTrack = payload[21];
        var trackCount = lastTrack >= firstTrack ? lastTrack - firstTrack + 1 : 1;
        for (var index = 0; index < trackCount; index++)
        {
            var offset = headerSize + index * trackSize;
            if (offset + trackSize > payload.Length)
            {
                break;
            }

            var sectorSize = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 12, 2));
            var mode = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 14, 2));
            var index1 = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(offset + 26, 8));
            var end = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(offset + 34, 8));
            AddCandidate(candidates, 4, index1, end, mode, sectorSize, dataBoundary);
        }
    }

    private static void ReadDaoi(Stream source, long payloadOffset, int payloadSize, long dataBoundary,
        List<TrackCandidate> candidates)
    {
        const int headerSize = 24;
        const int trackSize = 32;
        if (payloadSize < headerSize + trackSize)
        {
            return;
        }

        var payload = ReadPayload(source, payloadOffset, payloadSize);
        if (payload is null)
        {
            return;
        }

        var firstTrack = payload[22];
        var lastTrack = payload[23];
        var trackCount = lastTrack >= firstTrack ? lastTrack - firstTrack + 1 : 1;
        for (var index = 0; index < trackCount; index++)
        {
            var offset = headerSize + index * trackSize;
            if (offset + trackSize > payload.Length)
            {
                break;
            }

            var sectorSize = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 12, 4));
            var mode = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 16, 4));
            var index1 = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 24, 4));
            var end = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 28, 4));
            AddCandidate(candidates, 2, index1, end, mode, sectorSize, dataBoundary);
        }
    }

    private static void ReadEtn2(Stream source, long payloadOffset, int payloadSize, long dataBoundary,
        List<TrackCandidate> candidates)
    {
        const int trackSize = 32;
        var payload = ReadPayload(source, payloadOffset, payloadSize);
        if (payload is null)
        {
            return;
        }

        for (var offset = 0; offset + trackSize <= payload.Length; offset += trackSize)
        {
            var trackOffset = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(offset, 8));
            var length = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(offset + 8, 8));
            var mode = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 16, 4));
            AddCandidate(candidates, 3, (long)trackOffset, (long)(trackOffset + length), mode, 0, dataBoundary);
        }
    }

    private static void ReadEtnf(Stream source, long payloadOffset, int payloadSize, long dataBoundary,
        List<TrackCandidate> candidates)
    {
        const int trackSize = 20;
        var payload = ReadPayload(source, payloadOffset, payloadSize);
        if (payload is null)
        {
            return;
        }

        for (var offset = 0; offset + trackSize <= payload.Length; offset += trackSize)
        {
            var trackOffset = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4));
            var length = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 4, 4));
            var mode = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + 8, 4));
            AddCandidate(candidates, 1, trackOffset, (long)trackOffset + length, mode, 0, dataBoundary);
        }
    }

    private static byte[]? ReadPayload(Stream source, long payloadOffset, int payloadSize)
    {
        if (payloadSize <= 0 || payloadSize > MaxTrackChunkPayload)
        {
            return null;
        }

        var payload = new byte[payloadSize];
        source.Position = payloadOffset;
        ReadExactly(source, payload);
        return payload;
    }

    private static void AddCandidate(List<TrackCandidate> candidates, int priority, long start, long end, uint mode,
        uint declaredSectorSize, long dataBoundary)
    {
        if (!TryResolveSectorLayout(mode, out var sectorSize, out var dataOffsetInSector))
        {
            return;
        }

        if (declaredSectorSize > 0 && declaredSectorSize != sectorSize)
        {
            return;
        }

        if (start < 0 || end <= start || end > dataBoundary || end - start < sectorSize)
        {
            return;
        }

        candidates.Add(new TrackCandidate(priority, new NrgDataTrack(start, end, sectorSize, dataOffsetInSector)));
    }

    private static bool TryResolveSectorLayout(uint mode, out int sectorSize, out int dataOffsetInSector)
    {
        var normalized = mode > 0xFF ? mode >> 8 : mode;
        switch (normalized)
        {
            case 0x00:
                sectorSize = 2048;
                dataOffsetInSector = 0;
                return true;
            case 0x03:
                sectorSize = 2336;
                dataOffsetInSector = 8;
                return true;
            case 0x05:
                sectorSize = 2352;
                dataOffsetInSector = 16;
                return true;
            case 0x06:
                sectorSize = 2352;
                dataOffsetInSector = 24;
                return true;
            default:
                sectorSize = 0;
                dataOffsetInSector = 0;
                return false;
        }
    }

    private static NrgDataTrack SelectTrack(List<TrackCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new NrgImageFormatException("no supported data track was found.");
        }

        var best = candidates[0];
        foreach (var candidate in candidates)
        {
            if (candidate.Priority > best.Priority)
            {
                best = candidate;
            }
        }

        return best.Track;
    }

    private static void ReadExactly(Stream source, Span<byte> buffer)
    {
        try
        {
            source.ReadExactly(buffer);
        }
        catch (EndOfStreamException)
        {
            throw new NrgImageFormatException("the image ended unexpectedly.");
        }
    }

    private sealed record TrackCandidate(int Priority, NrgDataTrack Track);
}
