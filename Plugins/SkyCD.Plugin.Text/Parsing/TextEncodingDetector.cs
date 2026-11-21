using System;
using System.Text;

namespace SkyCD.Plugin.Text.Parsing;

/// <summary>The outcome of inspecting a source's leading bytes.</summary>
/// <param name="Name">Stable encoding identifier published through the schema.</param>
/// <param name="Encoding">Decoder for the payload after the preamble.</param>
/// <param name="PreambleLength">Byte order mark length in bytes.</param>
/// <param name="DeclaredByByteOrderMark">True when a BOM named the encoding rather than it being inferred.</param>
internal sealed record TextEncodingInfo(
    string Name,
    System.Text.Encoding Encoding,
    int PreambleLength,
    bool DeclaredByByteOrderMark);

/// <summary>
/// Identifies a source's character encoding and rejects binary content.
/// </summary>
/// <remarks>
/// A byte order mark is authoritative and wins. Without one, strict UTF-8 validation decides between UTF-8
/// and the documented ISO-8859-1 fallback; ISO-8859-1 maps every byte, so it never fails, which is why the
/// binary decision is made on the decoded characters rather than the raw bytes. Detecting binary on decoded
/// characters is also what lets UTF-16 sources pass: their NUL bytes are legitimate, and counting bytes
/// would reject every one of them.
/// </remarks>
internal static class TextEncodingDetector
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf32LeBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBom = [0x00, 0x00, 0xFE, 0xFF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    /// <summary>Strict UTF-8: throws on an invalid sequence instead of substituting a replacement character.</summary>
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TextEncodingInfo Detect(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, Utf32LeBom))
        {
            return new TextEncodingInfo("utf-32le", Encoding.UTF32, Utf32LeBom.Length, true);
        }

        if (StartsWith(data, Utf32BeBom))
        {
            return new TextEncodingInfo("utf-32be", Encoding.GetEncoding("utf-32BE"), Utf32BeBom.Length, true);
        }

        if (StartsWith(data, Utf8Bom))
        {
            return new TextEncodingInfo("utf-8", StrictUtf8, Utf8Bom.Length, true);
        }

        // Checked after UTF-32 little-endian, whose marker begins with the same two bytes.
        if (StartsWith(data, Utf16LeBom))
        {
            return new TextEncodingInfo("utf-16le", Encoding.Unicode, Utf16LeBom.Length, true);
        }

        if (StartsWith(data, Utf16BeBom))
        {
            return new TextEncodingInfo("utf-16be", Encoding.BigEndianUnicode, Utf16BeBom.Length, true);
        }

        return IsValidUtf8(TrimIncompleteUtf8(data))
            ? new TextEncodingInfo("utf-8", StrictUtf8, 0, false)
            : new TextEncodingInfo("iso-8859-1", Encoding.Latin1, 0, false);
    }

    /// <summary>
    /// Drops a trailing partial UTF-8 sequence. A bounded scan window can cut a multi-byte character in half,
    /// and without this the tail would fail strict validation and the whole source would be misreported as
    /// ISO-8859-1.
    /// </summary>
    internal static ReadOnlySpan<byte> TrimIncompleteUtf8(ReadOnlySpan<byte> payload)
    {
        // Only the last three bytes can belong to an unfinished sequence.
        for (var back = 1; back <= Math.Min(3, payload.Length); back++)
        {
            var index = payload.Length - back;
            var value = payload[index];
            if ((value & 0xC0) != 0xC0)
            {
                // A continuation byte; keep looking back for the lead byte.
                continue;
            }

            var expected = value >= 0xF0 ? 4 : value >= 0xE0 ? 3 : 2;
            return back >= expected ? payload : payload[..index];
        }

        return payload;
    }

    /// <summary>
    /// Counts characters that cannot appear in text, so a binary file renamed to <c>.txt</c> is rejected
    /// instead of being catalogued as mojibake that later matches searches on nonsense.
    /// </summary>
    public static int CountControlCharacters(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (IsNonTextControl(character))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Tab, line feed, carriage return and form feed are ordinary in text; every other C0 control character,
    /// delete, and the replacement character produced by a lossy decode are not.
    /// </summary>
    private static bool IsNonTextControl(char character) => character switch
    {
        '\t' or '\n' or '\r' or '\f' => false,
        '\uFFFD' => true,
        _ => character < ' ' || character == '\u007F'
    };

    private static bool IsValidUtf8(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return true;
        }

        try
        {
            StrictUtf8.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}
