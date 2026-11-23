using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>
/// Reads the largest decodable image from a macOS <c>.icns</c> icon container.
/// </summary>
/// <remarks>
/// An <c>.icns</c> file is an eight-byte header followed by chunks, each with a four-byte type and a
/// big-endian length that includes the eight-byte chunk header. Only chunks whose payload is a standalone
/// PNG or JPEG are decoded: the modern <c>ic07</c>–<c>ic14</c> and <c>icp4</c>–<c>icp6</c> types store exactly
/// that. The legacy RLE (<c>is32</c>, <c>il32</c>, <c>ih32</c>, <c>it32</c>) and 24-bit <c>ic04</c>/<c>ic05</c>
/// chunks are deliberately skipped rather than approximated, so a wrong icon is never published.
/// </remarks>
internal static class IcnsReader
{
    private const int HeaderBytes = 8;
    private const int ChunkHeaderBytes = 8;

    /// <summary>Cap on chunks inspected, so a crafted container cannot make the reader loop without end.</summary>
    private const int MaximumChunks = 256;

    private static ReadOnlySpan<byte> Signature => "icns"u8;

    /// <summary>True when the data starts with the <c>.icns</c> container signature.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderBytes && data[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Decodes the largest PNG or JPEG chunk that fits the existing byte and pixel bounds; null when the
    /// container carries no such chunk. Malformed containers are reported as null, never as an error.
    /// </summary>
    public static async Task<ExecutableIconImage?> TryReadAsync(ExecutableSource source,
        ExecutablePluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        if (source.Length < HeaderBytes)
        {
            return null;
        }

        byte[] header;
        try
        {
            header = await source.ReadAsync(0, HeaderBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutableReadException)
        {
            return null;
        }

        if (!HasSignature(header))
        {
            return null;
        }

        var declaredLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        if (declaredLength < HeaderBytes)
        {
            return null;
        }

        var end = Math.Min(declaredLength, source.Length);
        var candidates = new List<IconChunk>();
        var offset = (long)HeaderBytes;
        var chunks = 0;
        while (offset + ChunkHeaderBytes <= end && chunks < MaximumChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] chunkHeader;
            try
            {
                chunkHeader = await source.ReadAsync(offset, ChunkHeaderBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ExecutableReadException)
            {
                break;
            }

            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader.AsSpan(4));
            if (chunkLength < ChunkHeaderBytes || offset + chunkLength > end)
            {
                break;
            }

            var edge = ExpectedEdge(chunkHeader.AsSpan(0, 4));
            var payloadLength = (int)(chunkLength - ChunkHeaderBytes);
            if (edge > 0 && payloadLength > 0 && payloadLength <= options.MaxIconBytes)
            {
                candidates.Add(new IconChunk(edge, offset + ChunkHeaderBytes, payloadLength));
            }

            offset += chunkLength;
            chunks++;
        }

        // The most detailed chunk first; an oversized one is skipped by the pixel bound and the next is tried.
        candidates.Sort(static (left, right) =>
        {
            var edge = right.Edge.CompareTo(left.Edge);
            return edge != 0 ? edge : right.Length.CompareTo(left.Length);
        });

        var examined = 0;
        foreach (var candidate in candidates)
        {
            if (examined >= options.MaxIndexedIconImages)
            {
                break;
            }

            examined++;
            byte[] payload;
            try
            {
                payload = await source.ReadAsync(candidate.Offset, candidate.Length, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ExecutableReadException)
            {
                continue;
            }

            if (!ExecutableIconDecoder.HasEncodedImageSignature(payload))
            {
                continue;
            }

            try
            {
                return ExecutableIconDecoder.DecodeEncodedImage(payload,
                    ExecutablePluginOptions.MaximumIconPixels);
            }
            catch (ExecutableIconException)
            {
                // A damaged or oversized chunk removes only that candidate.
            }
        }

        return null;
    }

    /// <summary>The icon edge a chunk type is defined to carry; zero for types this reader does not decode.</summary>
    private static int ExpectedEdge(ReadOnlySpan<byte> type)
    {
        if (type.SequenceEqual("icp4"u8))
        {
            return 16;
        }

        if (type.SequenceEqual("icp5"u8))
        {
            return 32;
        }

        if (type.SequenceEqual("icp6"u8))
        {
            return 64;
        }

        if (type.SequenceEqual("ic11"u8))
        {
            return 32;
        }

        if (type.SequenceEqual("ic12"u8))
        {
            return 64;
        }

        if (type.SequenceEqual("ic07"u8))
        {
            return 128;
        }

        if (type.SequenceEqual("ic13"u8))
        {
            return 256;
        }

        if (type.SequenceEqual("ic08"u8))
        {
            return 256;
        }

        if (type.SequenceEqual("ic14"u8))
        {
            return 512;
        }

        if (type.SequenceEqual("ic09"u8))
        {
            return 512;
        }

        return type.SequenceEqual("ic10"u8) ? 1024 : 0;
    }

    private readonly record struct IconChunk(int Edge, long Offset, int Length);
}
