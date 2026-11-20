using System;
using System.Buffers.Binary;
using System.IO;

namespace SkyCD.Plugin.Executable;

/// <summary>A decoded raster: 8-bit straight (non-premultiplied) RGBA rows, top to bottom.</summary>
internal sealed record PngImage(int Width, int Height, byte[] Rgba)
{
    /// <summary>The raster width; the canvas edge when the image is placed on one.</summary>
    public int PixelWidth => Width;

    /// <summary>The raster height; the canvas edge when the image is placed on one.</summary>
    public int PixelHeight => Height;

    /// <summary>The color of pixel (<paramref name="x"/>, <paramref name="y"/>) as a packed RGBA value.</summary>
    public uint PixelAt(int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return ((uint)Rgba[offset] << 24) | ((uint)Rgba[offset + 1] << 16) | ((uint)Rgba[offset + 2] << 8) |
               Rgba[offset + 3];
    }
}

/// <summary>
/// A minimal, bounds-checked PNG decoder for the shapes icon resources use.
/// </summary>
/// <remarks>
/// Icons are stored as PNG images that are 8-bit, non-interlaced and of any color type; those are exactly the
/// variants Windows and icon tooling emit. Anything else — 16-bit depth, palette transparency quirks, Adam7
/// interlacing — is rejected instead of approximated, because a wrong icon is worse than a type fallback. The
/// caller supplies the pixel bound, so a header claiming huge dimensions is refused before any row is decoded.
/// </remarks>
internal static class PngDecoder
{
    private const int MaximumChunkBytes = 64 * 1024 * 1024;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>True when the data starts with the PNG signature.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>
    /// Decodes the image. <paramref name="maximumPixels"/> bounds width times height; an image above it raises
    /// <see cref="InvalidDataException"/> without allocating the raster.
    /// </summary>
    public static PngImage Decode(ReadOnlySpan<byte> data, int maximumPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixels);

        if (!HasSignature(data))
        {
            throw new InvalidDataException("The data does not start with a PNG signature.");
        }

        var position = 8;
        var sawHeader = false;
        var sawEnd = false;
        int width = 0;
        int height = 0;
        var bitDepth = 0;
        var colorType = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        var compressed = new MemoryStream();

        while (!sawEnd)
        {
            if (data.Length - position < 12)
            {
                throw new InvalidDataException("The PNG chunk stream is truncated.");
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(position, 4));
            if (length < 0 || length > MaximumChunkBytes || data.Length - position - 12 < length)
            {
                throw new InvalidDataException("A PNG chunk length is not valid.");
            }

            var type = data.Slice(position + 4, 4);
            var body = data.Slice(position + 8, length);
            position += 12 + length;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || length != 13)
                {
                    throw new InvalidDataException("The PNG header chunk is not valid.");
                }

                sawHeader = true;
                width = BinaryPrimitives.ReadInt32BigEndian(body.Slice(0, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(body.Slice(4, 4));
                bitDepth = body[8];
                colorType = body[9];
                if (body[10] != 0)
                {
                    throw new InvalidDataException("Unsupported PNG compression method.");
                }

                if (body[11] != 0)
                {
                    throw new InvalidDataException("Unsupported PNG filter method.");
                }

                if (body[12] != 0)
                {
                    throw new InvalidDataException("Interlaced PNG images are not supported.");
                }

                ValidateShape(width, height, bitDepth, colorType, maximumPixels);
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (length == 0 || length % 3 != 0)
                {
                    throw new InvalidDataException("The PNG palette is not valid.");
                }

                palette = body.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = body.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader)
                {
                    throw new InvalidDataException("The PNG image data precedes its header.");
                }

                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                sawEnd = true;
            }
            else if ((type[0] & 0x20) == 0)
            {
                // An unknown critical chunk means the image cannot be interpreted as presented.
                throw new InvalidDataException("Unsupported critical PNG chunk.");
            }
        }

        if (!sawHeader || compressed.Length == 0)
        {
            throw new InvalidDataException("The PNG image has no image data.");
        }

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException("The PNG color type is not supported.")
        };

        if (colorType == 3 && palette is null)
        {
            throw new InvalidDataException("A palette PNG has no palette.");
        }

        var stride = checked(width * channels);
        var expected = checked((long)(stride + 1) * height);
        if (expected > int.MaxValue)
        {
            throw new InvalidDataException("The PNG image is too large to decode.");
        }

        var raw = ZlibStreamDecoder.Decode(compressed.GetBuffer(), 0, (int)expected);
        if (raw.Length < expected)
        {
            throw new InvalidDataException("The PNG image data is shorter than its dimensions require.");
        }

        return Unfilter(raw, width, height, channels, colorType, stride, palette, transparency);
    }

    private static void ValidateShape(int width, int height, int bitDepth, int colorType, int maximumPixels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The PNG dimensions are not valid.");
        }

        if ((long)width * height > maximumPixels)
        {
            throw new InvalidDataException("The PNG image exceeds the allowed pixel count.");
        }

        var allowedDepths = colorType switch
        {
            0 => new[] { 1, 2, 4, 8, 16 },
            2 => new[] { 8, 16 },
            3 => new[] { 1, 2, 4, 8 },
            4 => new[] { 8, 16 },
            6 => new[] { 8, 16 },
            _ => []
        };

        if (Array.IndexOf(allowedDepths, bitDepth) < 0)
        {
            throw new InvalidDataException("The PNG bit depth and color type combination is not valid.");
        }
    }

    private static PngImage Unfilter(byte[] raw, int width, int height, int channels, int colorType, int stride,
        byte[]? palette, byte[]? transparency)
    {
        var pixels = new byte[height * stride];
        for (var row = 0; row < height; row++)
        {
            var source = raw.AsSpan((row * (stride + 1)) + 1, stride);
            var target = pixels.AsSpan(row * stride, stride);
            var previous = row == 0 ? default : pixels.AsSpan((row - 1) * stride, stride);
            switch (raw[row * (stride + 1)])
            {
                case 0:
                    source.CopyTo(target);
                    break;
                case 1:
                    for (var index = 0; index < stride; index++)
                    {
                        var left = index >= channels ? target[index - channels] : 0;
                        target[index] = (byte)(source[index] + left);
                    }

                    break;
                case 2:
                    for (var index = 0; index < stride; index++)
                    {
                        target[index] = (byte)(source[index] + previous[index]);
                    }

                    break;
                case 3:
                    for (var index = 0; index < stride; index++)
                    {
                        var left = index >= channels ? target[index - channels] : 0;
                        target[index] = (byte)(source[index] + ((left + previous[index]) >> 1));
                    }

                    break;
                case 4:
                    for (var index = 0; index < stride; index++)
                    {
                        var left = index >= channels ? target[index - channels] : 0;
                        var up = previous[index];
                        var upLeft = index >= channels ? previous[index - channels] : 0;
                        target[index] = (byte)(source[index] + Paeth(left, up, upLeft));
                    }

                    break;
                default:
                    throw new InvalidDataException("The PNG row filter is not valid.");
            }
        }

        return new PngImage(width, height, ToRgba(pixels, width, height, channels, colorType, stride, palette,
            transparency));
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);
        return leftDistance <= upDistance && leftDistance <= upLeftDistance
            ? left
            : upDistance <= upLeftDistance
                ? up
                : upLeft;
    }

    private static byte[] ToRgba(byte[] pixels, int width, int height, int channels, int colorType, int stride,
        byte[]? palette, byte[]? transparency)
    {
        var rgba = new byte[width * height * 4];
        if (colorType is 4 or 6)
        {
            // Gray+alpha and RGBA, the shapes icon PNGs use most: converted and copied row by row.
            for (var row = 0; row < height; row++)
            {
                var source = pixels.AsSpan(row * stride, stride);
                var target = rgba.AsSpan(row * width * 4, width * 4);
                for (var column = 0; column < width; column++)
                {
                    var offset = column * 4;
                    if (colorType == 6)
                    {
                        source.Slice(column * 4, 4).CopyTo(target.Slice(offset, 4));
                        continue;
                    }

                    var gray = source[column * 2];
                    target[offset] = gray;
                    target[offset + 1] = gray;
                    target[offset + 2] = gray;
                    target[offset + 3] = source[(column * 2) + 1];
                }
            }

            return rgba;
        }

        for (var row = 0; row < height; row++)
        {
            var source = pixels.AsSpan(row * stride, stride);
            var target = rgba.AsSpan(row * width * 4, width * 4);
            for (var column = 0; column < width; column++)
            {
                var offset = column * 4;
                switch (colorType)
                {
                    case 0:
                        {
                            var gray = source[column];
                            target[offset] = gray;
                            target[offset + 1] = gray;
                            target[offset + 2] = gray;
                            target[offset + 3] = transparency is { Length: >= 2 } && TransparencySample(gray, transparency) == gray
                                ? (byte)0
                                : (byte)255;
                            break;
                        }

                    case 2:
                        {
                            var red = source[column * 3];
                            var green = source[(column * 3) + 1];
                            var blue = source[(column * 3) + 2];
                            target[offset] = red;
                            target[offset + 1] = green;
                            target[offset + 2] = blue;
                            target[offset + 3] = transparency is { Length: >= 6 } &&
                                                 red == transparency[1] && green == transparency[3] &&
                                                 blue == transparency[5]
                                ? (byte)0
                                : (byte)255;
                            break;
                        }

                    default:
                        {
                            var index = source[column];
                            var paletteOffset = index * 3;
                            if (palette is null || paletteOffset + 2 >= palette.Length)
                            {
                                throw new InvalidDataException("A PNG palette index is out of range.");
                            }

                            target[offset] = palette[paletteOffset];
                            target[offset + 1] = palette[paletteOffset + 1];
                            target[offset + 2] = palette[paletteOffset + 2];
                            target[offset + 3] = transparency is not null && index < transparency.Length
                                ? transparency[index]
                                : (byte)255;
                            break;
                        }
                }
            }
        }

        return rgba;
    }

    private static int TransparencySample(byte gray, byte[] transparency) => transparency[1];

    /// <summary>Encodes straight RGBA rows as an 8-bit, truecolor-with-alpha PNG.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException("The raster is shorter than its dimensions require.", nameof(rgba));
        }

        var stride = width * 4;
        var filtered = new byte[(stride + 1) * height];
        for (var row = 0; row < height; row++)
        {
            var source = rgba.Slice(row * stride, stride);
            var target = filtered.AsSpan(row * (stride + 1));
            target[0] = 0;
            source.CopyTo(target[1..]);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed,
                   System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(body);

        var crc = Crc32.Compute(type, body);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        stream.Write(checksum);
    }
}

/// <summary>The CRC-32 variant PNG uses for chunk checksums.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    /// <summary>Computes the CRC over two consecutive spans, as a chunk type followed by its body.</summary>
    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;
        crc = Update(crc, first);
        crc = Update(crc, second);
        return ~crc;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Compute(data, []);

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
