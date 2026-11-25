using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;
using SkyCD.Plugin.Metadata.WordPerfect.Models;

namespace SkyCD.Plugin.Metadata.WordPerfect.Parsing;

/// <summary>The WordPerfect stream bytes and how the source stored them.</summary>
internal sealed record WordPerfectSource(byte[] Data, string Storage);

/// <summary>
/// Reads a bounded source into memory and takes the WordPerfect stream out of a compound file when present.
/// </summary>
/// <remarks>
/// Reading in one pass keeps the host's budget accounting simple: the charge equals the source size, once.
/// The plugin only ever holds bytes and never a path, so a document that references another file has no way
/// to make it open that file.
/// </remarks>
internal static class WordPerfectSourceReader
{
    private const int ChunkBytes = 256 * 1024;
    private const string WordPerfectStreamName = "PerfectOffice_MAIN";

    public static async Task<WordPerfectSource> ReadAllAsync(
        MediaItem item,
        IMediaSource source,
        long maxSourceBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new WordPerfectDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new WordPerfectSourceTooLargeException(length, maxSourceBytes);
        }

        var buffer = new byte[length];
        var offset = 0L;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = (int)Math.Min(ChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new WordPerfectDecodeException(
                    $"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        if (CompoundFileReader.HasSignature(buffer))
        {
            return new WordPerfectSource(
                CompoundFileReader.ReadStream(buffer, WordPerfectStreamName, maxSourceBytes),
                "compound");
        }

        if (!WordPerfectDocument.HasMagic(buffer))
        {
            throw new WordPerfectDecodeException("the source is not a WordPerfect document");
        }

        return new WordPerfectSource(buffer, "flat");
    }
}
