using System;
using System.Buffers.Binary;
using System.IO;
using SkyCD.Plugin.Executable.Exceptions;
using SkyCD.Plugin.Executable.Models;

namespace SkyCD.Plugin.Executable.Parsing;

/// <summary>
/// Turns one icon resource into a square RGBA image.
/// </summary>
/// <remarks>
/// An icon resource is either a full bitmap (BITMAPINFOHEADER plus palette, XOR pixels and a 1-bit AND mask)
/// or, since Windows Vista, a bare PNG file. In a bitmap, an image that declares at least 32 bits per pixel
/// carries its own alpha channel; otherwise transparency comes from the AND mask, which is still applied to a
/// 32-bit image so the rounded corners of older icons survive.
/// </remarks>
internal static class ExecutableIconDecoder
{
    /// <summary>
    /// Decodes the resource at its own pixel size. Fitting it to the preview edge is the encoder's job; an
    /// image above <paramref name="maximumPixels"/> raises <see cref="ExecutableIconException"/> before its
    /// raster is allocated.
    /// </summary>
    public static ExecutableIconImage Decode(ReadOnlySpan<byte> data, int maximumPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixels);

        if (PngDecoder.HasSignature(data))
        {
            var png = PngDecoder.Decode(data, maximumPixels);
            return new ExecutableIconImage(png.Width, png.Height, png.Rgba);
        }

        return DecodeBitmap(data, maximumPixels);
    }

    /// <summary>Reads the pixel dimensions an icon bitmap declares, or null when it is not a readable bitmap.</summary>
    public static (int Width, int Height, int BitCount)? ReadBitmapHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || PngDecoder.HasSignature(data))
        {
            return null;
        }

        int headerSize;
        try
        {
            headerSize = FindHeaderOffset(data);
        }
        catch (ExecutableIconException)
        {
            return null;
        }

        if ((long)headerSize + 16 > data.Length)
        {
            return null;
        }

        var header = data.Slice(headerSize);
        int width;
        int height;
        int bitCount;
        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));
        }
        else
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));
            height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8, 4));
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(14, 2));
        }

        // An icon bitmap stacks its XOR and AND halves, so the declared height is twice the icon's.
        height /= 2;
        return width <= 0 || height <= 0 ? null : (width, height, bitCount);
    }

    private static ExecutableIconImage DecodeBitmap(ReadOnlySpan<byte> data, int maximumPixels)
    {
        var header = ReadBitmapHeader(data);
        if (header is not { } dimensions || (long)dimensions.Width * dimensions.Height > maximumPixels)
        {
            throw new ExecutableIconException("the icon resource is not a supported bitmap or PNG");
        }

        var (width, height, bitCount) = dimensions;
        if (bitCount is not (1 or 2 or 4 or 8 or 16 or 24 or 32))
        {
            throw new ExecutableIconException($"the icon bitmap uses {bitCount} bits per pixel");
        }

        var paletteEntries = bitCount <= 8 ? 1 << bitCount : 0;

        // The icon directory pads every entry to a 16-byte boundary counted from the start of the file, and
        // Windows writes the same alignment into resource data, so a header may start after the data begins.
        var headerSize = FindHeaderOffset(data);
        var paletteOffset = headerSize;
        var xorOffset = paletteOffset + (paletteEntries * 4);
        var maskStride = ((width + 31) / 32) * 4;
        var xorStride = (((width * bitCount) + 31) / 32) * 4;

        if ((long)xorOffset + ((long)xorStride * height) + ((long)maskStride * height) > data.Length)
        {
            throw new ExecutableIconException("the icon bitmap is truncated");
        }

        var rgba = new byte[width * height * 4];
        var hasAlphaChannel = bitCount == 32;

        for (var row = 0; row < height; row++)
        {
            // Bitmaps are stored bottom-up.
            var sourceRow = xorOffset + ((long)(height - 1 - row) * xorStride);
            var maskRow = xorOffset + ((long)xorStride * height) + ((long)(height - 1 - row) * maskStride);
            var targetRow = row * width * 4;

            for (var column = 0; column < width; column++)
            {
                var target = targetRow + (column * 4);
                // Unreachable, but the compiler needs a value for every path.
                byte red;
                byte green;
                byte blue;
                var alpha = (byte)255;
                switch (bitCount)
                {
                    case 1 or 2 or 4 or 8:
                        {
                            var paletteIndex = ReadPaletteIndex(data.Slice((int)sourceRow, xorStride), column, bitCount);
                            var entry = paletteOffset + (paletteIndex * 4);
                            blue = data[entry];
                            green = data[entry + 1];
                            red = data[entry + 2];
                            break;
                        }

                    case 16:
                        {
                            var value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice((int)sourceRow + (column * 2), 2));
                            red = (byte)(((value >> 10) & 0x1F) * 255 / 31);
                            green = (byte)(((value >> 5) & 0x1F) * 255 / 31);
                            blue = (byte)((value & 0x1F) * 255 / 31);
                            break;
                        }

                    case 24:
                        {
                            var offset = (int)sourceRow + (column * 3);
                            blue = data[offset];
                            green = data[offset + 1];
                            red = data[offset + 2];
                            break;
                        }

                    case 32:
                        {
                            var offset = (int)sourceRow + (column * 4);
                            blue = data[offset];
                            green = data[offset + 1];
                            red = data[offset + 2];
                            alpha = data[offset + 3];
                            break;
                        }

                    default:
                        throw new ExecutableIconException($"the icon bitmap uses {bitCount} bits per pixel");
                }

                var masked = (data[(int)maskRow + (column >> 3)] & (0x80 >> (column & 7))) != 0;
                if (!hasAlphaChannel || alpha == 0)
                {
                    alpha = masked ? (byte)0 : (byte)255;
                }
                else if (masked && alpha == 255)
                {
                    alpha = 0;
                }

                rgba[target] = red;
                rgba[target + 1] = green;
                rgba[target + 2] = blue;
                rgba[target + 3] = alpha;
            }
        }

        return new ExecutableIconImage(width, height, rgba);
    }

    /// <summary>
    /// Returns the offset of the bitmap header: zero, or the four-byte value at zero when it points at a
    /// <c>BITMAPCOREHEADER</c> or <c>BITMAPINFOHEADER</c> inside the same resource.
    /// </summary>
    private static int FindHeaderOffset(ReadOnlySpan<byte> data)
    {
        var declared = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (declared is 12 or 40 or 52 or 56 or 64 or 108 or 124)
        {
            return 0;
        }

        if (declared >= 0 && (long)declared + 16 <= data.Length)
        {
            var candidate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(declared));
            if (candidate is 12 or 40 or 52 or 56 or 64 or 108 or 124)
            {
                return declared;
            }
        }

        throw new ExecutableIconException("the icon resource has no recognizable bitmap header");
    }

    private static int ReadPaletteIndex(ReadOnlySpan<byte> row, int column, int bitCount)
    {
        switch (bitCount)
        {
            case 8:
                return row[column];
            case 4:
                return (row[column >> 1] >> (column % 2 == 0 ? 4 : 0)) & 0x0F;
            case 2:
                return (row[column >> 2] >> (6 - ((column % 4) * 2))) & 0x03;
            default:
                return (row[column >> 3] >> (7 - (column % 8))) & 0x01;
        }
    }
}
