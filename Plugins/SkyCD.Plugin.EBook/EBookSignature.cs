using System;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Recognizes the container flavors and cover image encodings from their leading bytes. Detection works on
/// the bytes, never on the file extension, so a mislabelled source is treated as what it is.
/// </summary>
internal static class EBookSignature
{
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipEmpty = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Bmp = [0x42, 0x4D];

    public static bool IsZip(ReadOnlySpan<byte> data) =>
        data.StartsWith(Zip) || data.StartsWith(ZipEmpty);

    public static bool IsXml(ReadOnlySpan<byte> data)
    {
        // A BOM or leading whitespace is legal before the XML declaration.
        var index = data.StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        while (index < data.Length && data[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            index++;
        }

        return data[index..].StartsWith("<"u8);
    }

    /// <summary>True when the bytes start like a raster image SkiaSharp can decode from a book.</summary>
    public static bool IsImage(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return false;
        }

        return data.StartsWith(Jpeg)
               || data.StartsWith(Png)
               || data.StartsWith("GIF8"u8)
               || data.StartsWith(Bmp)
               || (data.StartsWith("RIFF"u8) && data[8..].StartsWith("WEBP"u8));
    }

    /// <summary>
    /// Finds the start of an image that a Kindle resource record wraps. The record may lead with a
    /// <c>RESC</c> header or another small prefix, so the signature is searched only in the leading bytes.
    /// </summary>
    public static int FindImageStart(ReadOnlySpan<byte> data, int searchLimit = 64)
    {
        var limit = Math.Min(data.Length, searchLimit);
        for (var start = 0; start + 12 <= limit; start++)
        {
            if (IsImage(data[start..]))
            {
                return start;
            }
        }

        return -1;
    }
}
