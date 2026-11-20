using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkiaSharp;

namespace SkyCD.Plugin.Image.Tests;

/// <summary>
/// Builds the image fixtures the tests need, in memory, so the suite carries no binary blobs and every
/// fixture's exact properties are known to the test that asserts on them.
/// </summary>
internal static class ImageFixtures
{
    /// <summary>A transparent PNG: premultiplied alpha with a fully clear top-left quadrant.</summary>
    public static byte[] TransparentPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var fill = new SKPaint { Color = SKColors.SteelBlue };
        canvas.DrawRect(new SKRect(width / 2f, 0, width, height), fill);
        canvas.Flush();
        return Encode(bitmap, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>An opaque PNG with no alpha channel.</summary>
    public static byte[] OpaquePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkRed);
        canvas.Flush();
        return Encode(bitmap, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// A JPEG whose top-left quadrant is a distinct colour, so a test can tell whether an orientation was
    /// actually applied rather than merely reported.
    /// </summary>
    public static byte[] Jpeg(int width, int height)
    {
        // Bgra8888 rather than Rgb888x: Skia's JPEG encoder rejects the latter and returns no data.
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var fill = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(new SKRect(0, 0, width / 2f, height / 2f), fill);
        canvas.Flush();
        return Encode(bitmap, SKEncodedImageFormat.Jpeg, 90);
    }

    public static byte[] Webp(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkGreen);
        canvas.Flush();
        return Encode(bitmap, SKEncodedImageFormat.Webp, 90);
    }

    /// <summary>
    /// A JPEG carrying an EXIF APP1 segment with the given orientation and, optionally, a capture date in
    /// both <c>DateTimeOriginal</c> and the IFD0 <c>DateTime</c> fallback.
    /// </summary>
    public static byte[] JpegWithExif(int width, int height, ushort orientation, DateTime? capturedAt = null)
    {
        var jpeg = Jpeg(width, height);
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidOperationException("The encoder did not produce a JPEG.");
        }

        var app1 = BuildExifApp1Segment(orientation, capturedAt);
        var result = new byte[2 + app1.Length + (jpeg.Length - 2)];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        Buffer.BlockCopy(app1, 0, result, 2, app1.Length);
        Buffer.BlockCopy(jpeg, 2, result, 2 + app1.Length, jpeg.Length - 2);
        return result;
    }

    /// <summary>
    /// A looping animated GIF. Frames are single-colour blocks of the global colour table, and the LZW stream
    /// emits literals only, which every decoder accepts.
    /// </summary>
    public static byte[] AnimatedGif(int width, int height, int frameCount)
    {
        if (frameCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "An animated GIF needs at least two frames.");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
        writer.Write((ushort)width);
        writer.Write((ushort)height);

        // Global colour table present, 8-bit colour resolution, table size 0 -> two entries.
        writer.Write((byte)0xF0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(new byte[] { 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF });

        // NETSCAPE2.0 application extension: loop forever.
        writer.Write(new byte[] { 0x21, 0xFF, 0x0B });
        writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        writer.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

        for (var frame = 0; frame < frameCount; frame++)
        {
            // Graphic control extension: disposal "do not dispose", delay in hundredths of a second.
            writer.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 });
            writer.Write((ushort)((frame + 1) * 10));
            writer.Write(new byte[] { 0x00, 0x00 });

            // Image descriptor covering the whole logical screen, no local colour table, not interlaced.
            writer.Write((byte)0x2C);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)width);
            writer.Write((ushort)height);
            writer.Write((byte)0x00);

            writer.Write(LzwImageData(width * height));
        }

        writer.Write((byte)0x3B);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// An XMP packet with the properties file managers share. The packet is a string rather than a fixture so
    /// each test states exactly which keywords, caption and rating it expects to read back.
    /// </summary>
    public static string Xmp(
        IReadOnlyList<string>? keywords = null,
        string? description = null,
        int? rating = null,
        string? pdfKeywords = null)
    {
        var builder = new StringBuilder();
        builder.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        builder.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        builder.Append("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        builder.Append("<rdf:Description rdf:about=\"\"");
        builder.Append(" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
        builder.Append(" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        builder.Append(" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">");

        if (keywords is not null)
        {
            builder.Append("<dc:subject><rdf:Bag>");
            foreach (var keyword in keywords)
            {
                builder.Append("<rdf:li>").Append(Escape(keyword)).Append("</rdf:li>");
            }

            builder.Append("</rdf:Bag></dc:subject>");
        }

        if (description is not null)
        {
            builder.Append("<dc:description><rdf:Alt>");
            builder.Append("<rdf:li xml:lang=\"x-default\">").Append(Escape(description)).Append("</rdf:li>");
            builder.Append("</rdf:Alt></dc:description>");
        }

        if (rating is not null)
        {
            builder.Append("<xmp:Rating>").Append(rating.Value).Append("</xmp:Rating>");
        }

        if (pdfKeywords is not null)
        {
            builder.Append("<pdf:Keywords>").Append(Escape(pdfKeywords)).Append("</pdf:Keywords>");
        }

        builder.Append("</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");
        return builder.ToString();
    }

    /// <summary>Inserts an APP1 XMP segment after the JPEG start marker.</summary>
    public static byte[] WithXmp(byte[] jpeg, string packet) =>
        InsertAfterStartOfImage(jpeg, [0xFF, 0xE1],
            [.. Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0"), .. Encoding.UTF8.GetBytes(packet)]);

    /// <summary>Inserts a COM comment segment after the JPEG start marker.</summary>
    public static byte[] WithComment(byte[] jpeg, string comment) =>
        InsertAfterStartOfImage(jpeg, [0xFF, 0xFE], Encoding.Latin1.GetBytes(comment));

    /// <summary>Inserts an APP13 Photoshop block carrying the given IPTC-IIM datasets.</summary>
    public static byte[] WithIptc(byte[] jpeg, params (byte Dataset, string Value)[] records)
    {
        using var iim = new MemoryStream();
        foreach (var (dataset, value) in records)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            iim.Write([0x1C, 0x02, dataset, (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF)]);
            iim.Write(bytes);
        }

        var iimBytes = iim.ToArray();
        using var block = new MemoryStream();
        block.Write(Encoding.ASCII.GetBytes("8BIM"));
        block.Write([0x04, 0x04, 0x00, 0x00]);
        block.Write([(byte)(iimBytes.Length >> 24), (byte)(iimBytes.Length >> 16),
            (byte)(iimBytes.Length >> 8), (byte)iimBytes.Length]);
        block.Write(iimBytes);
        if ((iimBytes.Length & 1) == 1)
        {
            block.WriteByte(0x00);
        }

        return InsertAfterStartOfImage(jpeg, [0xFF, 0xED],
            [.. Encoding.ASCII.GetBytes("Photoshop 3.0\0"), .. block.ToArray()]);
    }

    /// <summary>Inserts an uncompressed <c>tEXt</c> chunk after the PNG header chunk.</summary>
    public static byte[] WithPngText(byte[] png, string keyword, string text) =>
        InsertPngChunk(png, "tEXt", [.. Encoding.Latin1.GetBytes(keyword), 0x00,
            .. Encoding.Latin1.GetBytes(text)]);

    /// <summary>Inserts an uncompressed international <c>iTXt</c> chunk carrying an XMP packet.</summary>
    public static byte[] WithPngXmp(byte[] png, string packet) =>
        InsertPngChunk(png, "iTXt", [.. Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0\0\0\0\0"),
            .. Encoding.UTF8.GetBytes(packet)]);

    /// <summary>Appends a RIFF <c>XMP </c> chunk and fixes the container size field.</summary>
    public static byte[] WithWebpXmp(byte[] webp, string packet)
    {
        var payload = Encoding.UTF8.GetBytes(packet);
        var padded = payload.Length + (payload.Length & 1);
        var result = new byte[webp.Length + 8 + padded];
        webp.CopyTo(result, 0);
        var position = webp.Length;
        result[position] = (byte)'X';
        result[position + 1] = (byte)'M';
        result[position + 2] = (byte)'P';
        result[position + 3] = (byte)' ';

        var size = payload.Length;
        result[position + 4] = (byte)size;
        result[position + 5] = (byte)(size >> 8);
        result[position + 6] = (byte)(size >> 16);
        result[position + 7] = (byte)(size >> 24);
        payload.CopyTo(result, position + 8);

        var riffSize = result.Length - 8;
        result[4] = (byte)riffSize;
        result[5] = (byte)(riffSize >> 8);
        result[6] = (byte)(riffSize >> 16);
        result[7] = (byte)(riffSize >> 24);
        return result;
    }

    /// <summary>Bytes that are not an image at all, for malformed-input coverage.</summary>
    public static byte[] NotAnImage(int length = 64)
    {
        var bytes = new byte[length];
        new Random(20240301).NextBytes(bytes);
        return bytes;
    }

    /// <summary>A structurally valid image cut short, which decoders must reject rather than half-decode.</summary>
    public static byte[] Truncated(byte[] image) => image[..(image.Length / 3)];

    /// <summary>
    /// Points the EXIF IFD0 offset past the end of the data in place, producing a container whose pixels are
    /// fine but whose tag directory is not — the case where a reader must give up on tags, not on the image.
    /// </summary>
    public static void CorruptExifIfdOffset(byte[] image)
    {
        byte[] exifPrefix = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];

        for (var i = 0; i + exifPrefix.Length + 8 <= image.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < exifPrefix.Length; j++)
            {
                if (image[i + j] != exifPrefix[j])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            var tiff = i + exifPrefix.Length;
            for (var offset = 4; offset < 8; offset++)
            {
                image[tiff + offset] = 0xFF;
            }

            return;
        }

        throw new InvalidOperationException("The fixture carries no EXIF segment to corrupt.");
    }

    /// <summary>Reads back the pixel at a point of a decoded image, for orientation assertions.</summary>
    public static SKColor PixelAt(ReadOnlyMemory<byte> image, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(image.Span)
                          ?? throw new InvalidOperationException("The fixture could not be decoded by the test.");
        return bitmap.GetPixel(x, y);
    }

    public static (int Width, int Height) Dimensions(ReadOnlyMemory<byte> image)
    {
        using var bitmap = SKBitmap.Decode(image.Span)
                          ?? throw new InvalidOperationException("The fixture could not be decoded by the test.");
        return (bitmap.Width, bitmap.Height);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
                         ?? throw new InvalidOperationException($"The fixture could not be encoded as {format}.");
        return data.ToArray();
    }

    /// <summary>
    /// A complete GIF image data stream for a block of palette index 0: the LZW minimum code size, the
    /// sub-blocks and the terminator. A clear code precedes every pixel, which keeps the dictionary below
    /// the point where a decoder would widen the code size, so every code stays three bits wide.
    /// </summary>
    private static byte[] LzwImageData(int pixelCount)
    {
        const int clearCode = 4;
        const int endCode = 5;

        var codes = new List<int> { clearCode };
        for (var i = 0; i < pixelCount; i++)
        {
            codes.Add(0);
            codes.Add(clearCode);
        }

        codes.Add(endCode);

        var bits = new List<bool>();
        foreach (var code in codes)
        {
            for (var bit = 0; bit < 3; bit++)
            {
                bits.Add((code & (1 << bit)) != 0);
            }
        }

        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        var packed = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                packed[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        using var stream = new MemoryStream();
        stream.WriteByte(0x02);
        for (var offset = 0; offset < packed.Length; offset += 255)
        {
            var chunk = Math.Min(255, packed.Length - offset);
            stream.WriteByte((byte)chunk);
            stream.Write(packed, offset, chunk);
        }

        stream.WriteByte(0x00);
        return stream.ToArray();
    }

    private static byte[] BuildExifApp1Segment(ushort orientation, DateTime? capturedAt)
    {
        var tiff = BuildTiff(orientation, capturedAt);
        var payloadLength = 6 + tiff.Length;

        using var stream = new MemoryStream();
        stream.WriteByte(0xFF);
        stream.WriteByte(0xE1);
        stream.WriteByte((byte)((payloadLength + 2) >> 8));
        stream.WriteByte((byte)((payloadLength + 2) & 0xFF));
        stream.Write(Encoding.ASCII.GetBytes("Exif"));
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.Write(tiff, 0, tiff.Length);
        return stream.ToArray();
    }

    /// <summary>
    /// A little-endian TIFF with an IFD0 holding the orientation and, when a capture date is given, an Exif
    /// sub-IFD holding <c>DateTimeOriginal</c> plus the IFD0 <c>DateTime</c> fallback.
    /// </summary>
    private static byte[] BuildTiff(ushort orientation, DateTime? capturedAt)
    {
        var dateText = capturedAt?.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
        var hasDate = dateText is not null;

        const int tiffHeaderSize = 8;
        const int entrySize = 12;

        var ifd0Entries = 1 + (hasDate ? 2 : 0);
        var ifd0Start = tiffHeaderSize;
        var ifd0Size = 2 + (ifd0Entries * entrySize) + 4;
        var exifIfdStart = ifd0Start + ifd0Size;
        var exifIfdSize = hasDate ? 2 + entrySize + 4 : 0;
        var dataStart = exifIfdStart + exifIfdSize;
        var dateTimeOriginalOffset = dataStart;
        var dateTimeOffset = dataStart + 20;
        var total = dataStart + (hasDate ? 40 : 0);

        var buffer = new byte[total];
        using var stream = new MemoryStream(buffer, writable: true);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("II"));
        writer.Write((ushort)42);
        writer.Write(ifd0Start);

        stream.Position = ifd0Start;
        writer.Write((ushort)ifd0Entries);
        WriteEntry(writer, 0x0112, 3, 1, orientation);

        if (hasDate)
        {
            WriteEntry(writer, 0x0132, 2, 20, (uint)dateTimeOffset);
            WriteEntry(writer, 0x8769, 4, 1, (uint)exifIfdStart);
        }

        writer.Write(0u);

        if (hasDate)
        {
            stream.Position = exifIfdStart;
            writer.Write((ushort)1);
            WriteEntry(writer, 0x9003, 2, 20, (uint)dateTimeOriginalOffset);
            writer.Write(0u);

            WriteAscii(stream, dateTimeOriginalOffset, dateText!);
            WriteAscii(stream, dateTimeOffset, dateText!);
        }

        writer.Flush();
        return buffer;
    }

    private static void WriteEntry(BinaryWriter writer, ushort tag, ushort type, uint count, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);

        // SHORT values live in the low half of the four-byte value field on little-endian TIFF.
        if (type == 3)
        {
            writer.Write((ushort)value);
            writer.Write((ushort)0);
            return;
        }

        writer.Write(value);
    }

    private static void WriteAscii(Stream stream, int offset, string value)
    {
        stream.Position = offset;
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, Math.Min(bytes.Length, 19));
        stream.WriteByte(0);
    }

    private static byte[] InsertAfterStartOfImage(byte[] jpeg, byte[] marker, byte[] payload)
    {
        var length = payload.Length + 2;
        var header = new byte[] { marker[0], marker[1], (byte)(length >> 8), (byte)length };
        var result = new byte[jpeg.Length + header.Length + payload.Length];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        header.CopyTo(result, 2);
        payload.CopyTo(result, 2 + header.Length);
        Buffer.BlockCopy(jpeg, 2, result, 2 + header.Length + payload.Length, jpeg.Length - 2);
        return result;
    }

    /// <summary>Inserts a chunk directly after the 25-byte IHDR, which is always the first PNG chunk.</summary>
    private static byte[] InsertPngChunk(byte[] png, string type, byte[] payload)
    {
        var chunk = new byte[12 + payload.Length];
        chunk[0] = (byte)(payload.Length >> 24);
        chunk[1] = (byte)(payload.Length >> 16);
        chunk[2] = (byte)(payload.Length >> 8);
        chunk[3] = (byte)payload.Length;
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        var crc = Crc32(chunk.AsSpan(4, 4 + payload.Length));
        chunk[8 + payload.Length] = (byte)(crc >> 24);
        chunk[9 + payload.Length] = (byte)(crc >> 16);
        chunk[10 + payload.Length] = (byte)(crc >> 8);
        chunk[11 + payload.Length] = (byte)crc;

        const int insertAt = 8 + 12 + 13;
        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, insertAt).CopyTo(result);
        chunk.CopyTo(result, insertAt);
        png.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + chunk.Length));
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
