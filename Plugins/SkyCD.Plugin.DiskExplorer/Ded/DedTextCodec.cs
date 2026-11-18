using System;
using System.Text;
using SkyCD.Plugin.DiskExplorer.Ded.Exceptions;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Strict Windows-1252/UTF-8 codec for DED strings. Both encoder and decoder use
/// exception fallbacks so invalid legacy bytes fail with a typed diagnostic
/// instead of being replaced silently.
/// </summary>
public static class DedTextCodec
{
    private static readonly Encoding Windows1252;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static DedTextCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    /// <summary>Chooses Windows-1252 when representable, UTF-8 otherwise.</summary>
    public static DedText ChooseEncoding(string value)
    {
        try
        {
            _ = Windows1252.GetByteCount(value);
            return new DedText(value, DedStringEncoding.Windows1252);
        }
        catch (EncoderFallbackException)
        {
            return new DedText(value, DedStringEncoding.Utf8);
        }
    }

    /// <summary>Encodes a text field with its declared encoding.</summary>
    public static byte[] Encode(DedText text, string field)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Encoding switch
        {
            DedStringEncoding.Windows1252 => EncodeWindows1252(text.Value, field),
            DedStringEncoding.Utf8 => EncodeUtf8(text.Value, field),
            _ => throw new DedCorruptException($"text field '{field}' declares an unknown encoding.")
        };
    }

    /// <summary>Decodes a text field from its wire encoding value.</summary>
    public static DedText Decode(byte[] bytes, uint rawEncoding, string field)
    {
        return rawEncoding switch
        {
            0 => new DedText(DecodeWindows1252(bytes, field), DedStringEncoding.Windows1252),
            1 => new DedText(DecodeUtf8(bytes, field), DedStringEncoding.Utf8),
            _ => throw new DedCorruptException($"text field '{field}' declares an unknown encoding.")
        };
    }

    /// <summary>Encodes the fixed ANSI header title field with NUL padding.</summary>
    public static byte[] EncodeHeaderTitle(string title)
    {
        var bytes = EncodeWindows1252(title, "title");
        if (bytes.Length >= DedFormat.TitleSize)
        {
            throw new DedModelException(
                $"title exceeds {DedFormat.TitleSize - 1} Windows-1252 bytes.");
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new DedModelException("title contains an embedded NUL character.");
        }

        var field = new byte[DedFormat.TitleSize];
        Array.Copy(bytes, field, bytes.Length);
        return field;
    }

    /// <summary>Decodes a NUL-terminated ANSI header title field.</summary>
    public static string DecodeHeaderTitle(byte[] field)
    {
        var length = Array.IndexOf(field, (byte)0);
        if (length < 0)
        {
            length = field.Length;
        }

        var bytes = new byte[length];
        Array.Copy(field, bytes, length);
        return DecodeWindows1252(bytes, "title");
    }

    private static byte[] EncodeWindows1252(string value, string field)
    {
        try
        {
            return Windows1252.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw new DedTextEncodingException(field);
        }
    }

    private static byte[] EncodeUtf8(string value, string field)
    {
        try
        {
            return Utf8.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw new DedTextEncodingException(field);
        }
    }

    private static string DecodeWindows1252(byte[] bytes, string field)
    {
        try
        {
            return Windows1252.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new DedTextEncodingException(field);
        }
    }

    private static string DecodeUtf8(byte[] bytes, string field)
    {
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new DedTextEncodingException(field);
        }
    }
}
