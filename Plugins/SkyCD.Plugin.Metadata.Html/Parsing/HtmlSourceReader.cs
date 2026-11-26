using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Html.Exceptions;

namespace SkyCD.Plugin.Metadata.Html.Parsing;

/// <summary>
/// Reads a bounded source into memory and decodes it as text before the parser sees it.
/// </summary>
/// <remarks>
/// HTML is defined over text, so a source that carries no text is reported as a decode error rather
/// than parsed into an empty document. UTF-8 is the default; a UTF-16 byte-order mark selects UTF-16,
/// and bytes that are not valid UTF-8 fall back to Windows-1252 so legacy documents declared in a
/// single-byte charset still catalogue. Reading in one pass keeps the host's budget accounting simple:
/// the charge equals the document size, once. Because the plugin only ever holds bytes and never a
/// path, it cannot follow an external file reference.
/// </remarks>
internal static class HtmlSourceReader
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
            throw new HtmlDecodeException("the source is empty");
        }

        if (length > maxSourceBytes || length > int.MaxValue)
        {
            throw new HtmlSourceTooLargeException(length, maxSourceBytes);
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
                throw new HtmlDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        var text = Decode(buffer);
        if (text.Contains('\0'))
        {
            throw new HtmlDecodeException("the source contains binary data");
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
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8: legacy single-byte documents (Windows-1252 and its near neighbours)
            // still decode instead of failing, so a declared legacy charset catalogues.
            return SkyCD.Plugin.EncodingProfiles.LegacyEncoding.Windows1252.GetString(data, offset, data.Length - offset);
        }
    }

    private static string DecodeUnicode(byte[] data, int offset, Encoding encoding)
    {
        if ((data.Length - offset) % 2 != 0)
        {
            throw new HtmlDecodeException("the source has a truncated UTF-16 byte-order mark");
        }

        try
        {
            return encoding.GetString(data, offset, data.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new HtmlDecodeException("the source is not valid UTF-16 text", exception);
        }
    }
}
