using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.AbiWord.Exceptions;
using SkyCD.Plugin.Metadata.AbiWord.Models;

namespace SkyCD.Plugin.Metadata.AbiWord.Parsing;

internal static class AbiWordSourceReader
{
    public static async Task<byte[]> ReadAsync(
        MediaItem item, IMediaSource source, AbiWordPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new AbiWordDecodeException("the source is empty");
        }

        if (length > options.MaxSourceBytes)
        {
            throw new AbiWordLimitExceededException("source bytes", options.MaxSourceBytes);
        }

        var data = new byte[(int)length];
        for (var offset = 0; offset < data.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(64 * 1024, data.Length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new AbiWordDecodeException("the source returned an incomplete range");
            }

            chunk.Span.CopyTo(data.AsSpan(offset, count));
            offset += count;
        }

        if (data.Length < 2 || data[0] != 0x1f || data[1] != 0x8b)
        {
            if (data.Length > options.MaxDecompressedBytes)
            {
                throw new AbiWordLimitExceededException("decoded bytes", options.MaxDecompressedBytes);
            }

            return data;
        }

        try
        {
            using var compressed = new MemoryStream(data, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buffer.Length, (long)options.MaxDecompressedBytes - decoded.Length + 1);
                var read = await gzip.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (decoded.Length + read > options.MaxDecompressedBytes)
                {
                    throw new AbiWordLimitExceededException("decoded bytes", options.MaxDecompressedBytes);
                }

                decoded.Write(buffer, 0, read);
            }

            // GZipStream accepts missing trailers; native AbiWord writes one member with a size footer.
            if (data.Length < 18 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4)) != decoded.Length)
            {
                throw new AbiWordDecodeException("missing or inconsistent gzip size footer");
            }

            return decoded.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new AbiWordDecodeException("invalid gzip data", exception);
        }
    }
}
