using System;
using System.Text;

namespace SkyCD.Plugin.MediaMetadata;

/// <summary>
/// Decoding helpers shared by the container readers. Metadata written by different tools is inconsistently
/// labeled, so values are decoded leniently: an invalid UTF-8 sequence falls back to Latin-1 rather than
/// discarding the whole field.
/// </summary>
internal static class MetadataText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Decodes UTF-8 when the bytes are valid, otherwise Latin-1, and trims NUL padding.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        try
        {
            return Clean(StrictUtf8.GetString(bytes));
        }
        catch (ArgumentException)
        {
            return Clean(Latin1.GetString(bytes));
        }
    }

    /// <summary>Decodes an ASCII/Latin-1 field that some tools pad with NULs.</summary>
    public static string DecodeLatin1(ReadOnlySpan<byte> bytes) =>
        bytes.IsEmpty ? string.Empty : Clean(Latin1.GetString(bytes));

    /// <summary>Decodes a UTF-16LE field such as the Windows XP* TIFF tags.</summary>
    public static string DecodeUtf16LittleEndian(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
        {
            return string.Empty;
        }

        return Clean(Encoding.Unicode.GetString(bytes));
    }

    /// <summary>Replaces embedded NULs (UTF-16 padding decodes to them) and trims surrounding whitespace.</summary>
    public static string Clean(string? value) =>
        value is null ? string.Empty : value.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>Shortens a value to the schema's per-field limit without splitting a surrogate pair.</summary>
    public static string Cap(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    /// <summary>
    /// Normalizes one keyword: semicolons and line breaks become spaces because they would otherwise split or
    /// corrupt the semicolon-joined field the host reads.
    /// </summary>
    public static string NormalizeKeyword(string value) =>
        Clean(value).Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
