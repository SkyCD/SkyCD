using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Markdown.Exceptions;

namespace SkyCD.Plugin.Metadata.Markdown.Parsing;

/// <summary>
/// Reads a bounded source into memory and decodes it as text before any parser sees it.
/// </summary>
/// <remarks>
/// Markdown is defined over text, so a source that is not valid text is reported as a decode error rather
/// than parsed into mojibake. UTF-8 is the default; a UTF-16 byte-order mark selects UTF-16. Reading in one
/// pass keeps the host's budget accounting simple: the charge equals the document size, once. Because the
/// plugin only ever holds bytes and never a path, it cannot follow an external file reference.
/// </remarks>
internal static class MarkdownSourceReader
{
    private const int ChunkBytes = 256 * 1024;

    public static async Task<string> ReadTextAsync(
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
            throw new MarkdownDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new MarkdownSourceTooLargeException(length, maxSourceBytes);
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
                throw new MarkdownDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        var text = Decode(buffer);
        if (text.Contains('\0'))
        {
            throw new MarkdownDecodeException("the source contains binary data");
        }

        return text;
    }

    private static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return DecodeUnicode(data, 2, new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true));
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return DecodeUnicode(data, 2, new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true));
        }

        var offset = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(data, offset, data.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new MarkdownDecodeException("the source is not valid UTF-8 text", exception);
        }
    }

    private static string DecodeUnicode(byte[] data, int offset, Encoding encoding)
    {
        if ((data.Length - offset) % 2 != 0)
        {
            throw new MarkdownDecodeException("the source has a truncated UTF-16 byte-order mark");
        }

        try
        {
            return encoding.GetString(data, offset, data.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new MarkdownDecodeException("the source is not valid UTF-16 text", exception);
        }
    }
}
