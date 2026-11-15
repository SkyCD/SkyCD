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

    /// <summary>Bytes that are not an image at all, for malformed-input coverage.</summary>
    public static byte[] NotAnImage(int length = 64)
    {
        var bytes = new byte[length];
        new Random(20240301).NextBytes(bytes);
        return bytes;
    }

    /// <summary>A structurally valid image cut short, which decoders must reject rather than half-decode.</summary>
    public static byte[] Truncated(byte[] image) => image[..(image.Length / 3)];

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
}
