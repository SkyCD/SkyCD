using System;
using System.Buffers.Binary;
using System.Text;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads MonoGame/FNA <c>.xnb</c> headers and uncompressed Texture2D surfaces. LZ4/Deflate-compressed
/// payloads publish their header facts and no pixels instead of being decoded.
/// </summary>
internal static class MonoGameReader
{
    private const int HeaderBytes = 10;
    private const byte CompressedLz4 = 0x80;
    private const byte CompressedDeflate = 0x40;
    private const byte HiDefFlag = 0x01;

    internal static GameDevFacts? Read(byte[] data)
    {
        if (!TryReadHeader(data, out var header, out var readerName, out var payloadOffset))
        {
            return null;
        }

        var facts = new GameDevFacts
        {
            Family = "MonoGame",
            Format = "XNB",
            FormatVersion = header.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ItemName = readerName,
            Application = DescribePlatform(header.Platform) + (header.HiDef ? " HiDef" : " Reach"),
            Compiled = true
        };

        if (header.Compressed)
        {
            return facts;
        }

        if (readerName.EndsWith("Texture2DReader", StringComparison.Ordinal)
            && TryReadTexture(data, payloadOffset, out var texture))
        {
            facts.ImageWidth = texture.Width;
            facts.ImageHeight = texture.Height;
            facts.ColorMode = SurfaceFormatName(texture.Format);
            facts.HasThumbnail = texture.Pixels is not null;
        }

        return facts;
    }

    /// <summary>Decodes Texture2D surfaces in plain color formats; compressed surfaces yield no artifact.</summary>
    internal static byte[]? TryRender(byte[] data, out string contentType)
    {
        contentType = "image/png";
        if (!TryReadHeader(data, out var header, out var readerName, out var payloadOffset)
            || header.Compressed
            || !readerName.EndsWith("Texture2DReader", StringComparison.Ordinal)
            || !TryReadTexture(data, payloadOffset, out var texture)
            || texture.Pixels is null)
        {
            return null;
        }

        var rgba = ConvertToRgba(texture);
        if (rgba is null)
        {
            return null;
        }

        return PixelPreview.EncodeRgba(rgba, texture.Width, texture.Height,
            GameDevLimits.MaxThumbnailDimension, out contentType);
    }

    private static byte[]? ConvertToRgba(Texture texture)
    {
        var pixels = texture.Pixels;
        if (pixels is null)
        {
            return null;
        }

        var count = texture.Width * texture.Height;
        var rgba = new byte[count * 4];
        switch (texture.Format)
        {
            case 0: // Color: RGBA32
                if (pixels.Length < count * 4)
                {
                    return null;
                }

                Array.Copy(pixels, rgba, count * 4);
                return rgba;
            case 1: // Bgr565
                if (pixels.Length < count * 2)
                {
                    return null;
                }

                for (var index = 0; index < count; index++)
                {
                    var value = BinaryPrimitives.ReadUInt16LittleEndian(pixels.AsSpan(index * 2, 2));
                    rgba[index * 4] = (byte)(((value >> 11) & 0x1F) * 255 / 31);
                    rgba[(index * 4) + 1] = (byte)(((value >> 5) & 0x3F) * 255 / 63);
                    rgba[(index * 4) + 2] = (byte)((value & 0x1F) * 255 / 31);
                    rgba[(index * 4) + 3] = 255;
                }

                return rgba;
            case 2: // Bgra5551
                if (pixels.Length < count * 2)
                {
                    return null;
                }

                for (var index = 0; index < count; index++)
                {
                    var value = BinaryPrimitives.ReadUInt16LittleEndian(pixels.AsSpan(index * 2, 2));
                    rgba[index * 4] = (byte)(((value >> 10) & 0x1F) * 255 / 31);
                    rgba[(index * 4) + 1] = (byte)(((value >> 5) & 0x1F) * 255 / 31);
                    rgba[(index * 4) + 2] = (byte)((value & 0x1F) * 255 / 31);
                    rgba[(index * 4) + 3] = (byte)((value >> 15) == 1 ? 255 : 0);
                }

                return rgba;
            case 3: // Bgra4444
                if (pixels.Length < count * 2)
                {
                    return null;
                }

                for (var index = 0; index < count; index++)
                {
                    var value = BinaryPrimitives.ReadUInt16LittleEndian(pixels.AsSpan(index * 2, 2));
                    rgba[index * 4] = (byte)(((value >> 8) & 0x0F) * 17);
                    rgba[(index * 4) + 1] = (byte)(((value >> 4) & 0x0F) * 17);
                    rgba[(index * 4) + 2] = (byte)((value & 0x0F) * 17);
                    rgba[(index * 4) + 3] = (byte)(((value >> 12) & 0x0F) * 17);
                }

                return rgba;
            default:
                return null;
        }
    }

    private static bool TryReadHeader(byte[] data, out XnbHeader header, out string readerName,
        out int payloadOffset)
    {
        header = default;
        readerName = string.Empty;
        payloadOffset = 0;

        if (data.Length < HeaderBytes || !GameDevSource.StartsWithAscii(data, "XNB"))
        {
            return false;
        }

        var version = data[4];
        if (version is < 4 or > 5)
        {
            return false;
        }

        var flags = data[5];
        var declaredSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(6, 4));
        if (declaredSize < HeaderBytes || declaredSize > data.Length)
        {
            return false;
        }

        header = new XnbHeader((char)data[3], version, flags,
            (flags & HiDefFlag) != 0, (flags & (CompressedLz4 | CompressedDeflate)) != 0);

        var offset = HeaderBytes;
        if (header.Compressed)
        {
            // A compressed payload starts with its decompressed size; the compressed stream is not decoded.
            return true;
        }

        if (!TryRead7BitInt(data, ref offset, out var nameLength) || nameLength <= 0
            || nameLength > 1024 || offset + nameLength > data.Length)
        {
            return false;
        }

        readerName = Encoding.UTF8.GetString(data, offset, nameLength);
        offset += nameLength;

        if (offset + 4 > data.Length)
        {
            return false;
        }

        offset += 4;
        payloadOffset = offset;
        return true;
    }

    private static bool TryReadTexture(byte[] data, int offset, out Texture texture)
    {
        texture = default;
        if (offset + 16 > data.Length)
        {
            return false;
        }

        var format = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 8, 4));
        var mipCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 12, 4));
        if (format is < 0 or > 19 || width is <= 0 or > 16384 || height is <= 0 or > 16384 || mipCount <= 0)
        {
            return false;
        }

        offset += 16;
        if (offset + 4 > data.Length)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        if (size < 0 || size > data.Length - offset)
        {
            return false;
        }

        texture = new Texture(format, width, height, data.AsSpan(offset, size).ToArray());
        return true;
    }

    private static bool TryRead7BitInt(byte[] data, ref int offset, out int value)
    {
        value = 0;
        var shift = 0;
        while (offset < data.Length && shift < 35)
        {
            var current = data[offset++];
            value |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value >= 0;
            }

            shift += 7;
        }

        return false;
    }

    private static string DescribePlatform(char platform) => platform switch
    {
        'w' => "Windows",
        'x' => "Xbox",
        'm' => "Windows Phone",
        'i' => "iOS",
        'a' => "Android",
        _ => platform.ToString()
    };

    private static string SurfaceFormatName(int format) => format switch
    {
        0 => "Color",
        1 => "Bgr565",
        2 => "Bgra5551",
        3 => "Bgra4444",
        4 => "Dxt1",
        5 => "Dxt3",
        6 => "Dxt5",
        _ => "Format" + format.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private readonly record struct XnbHeader(char Platform, byte Version, byte Flags, bool HiDef, bool Compressed);

    private readonly record struct Texture(int Format, int Width, int Height, byte[]? Pixels);
}
