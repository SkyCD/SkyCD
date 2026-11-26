using System;
using AsepriteDotNet.Common;
using SkiaSharp;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Downsamples straight RGBA pixels and encodes them with the shared artifact encoder.</summary>
internal static class PixelPreview
{
    /// <summary>Encodes RGBA pixels without upscaling; PNG keeps alpha, JPEG is used for opaque images.</summary>
    internal static byte[]? EncodeRgba(ReadOnlySpan<Rgba32> pixels, int width, int height, int maxDimension,
        out string contentType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        var scale = Math.Min(1d, (double)maxDimension / Math.Max(width, height));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

        var scaled = new Rgba32[targetWidth * targetHeight];
        var hasAlpha = false;
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(height - 1, (int)(y / scale));
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(width - 1, (int)(x / scale));
                var pixel = pixels[sourceY * width + sourceX];
                scaled[y * targetWidth + x] = pixel;
                hasAlpha |= pixel.A != 255;
            }
        }

        var bytes = new byte[scaled.Length * 4];
        for (var index = 0; index < scaled.Length; index++)
        {
            var pixel = scaled[index];
            bytes[index * 4] = pixel.R;
            bytes[(index * 4) + 1] = pixel.G;
            bytes[(index * 4) + 2] = pixel.B;
            bytes[(index * 4) + 3] = pixel.A;
        }

        return Encode(bytes, targetWidth, targetHeight, hasAlpha, out contentType);
    }

    /// <summary>Encodes straight RGBA bytes without upscaling.</summary>
    internal static byte[]? EncodeRgba(byte[] rgba, int width, int height, int maxDimension, out string contentType)
    {
        var pixels = new Rgba32[width * height];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 4;
            pixels[index] = new Rgba32(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
        }

        return EncodeRgba(pixels, width, height, maxDimension, out contentType);
    }

    /// <summary>Encodes a decoded Skia image to PNG when it carries alpha, JPEG otherwise.</summary>
    internal static byte[]? EncodeImage(SKImage image, out string contentType, bool? hasAlpha = null)
    {
        var alpha = hasAlpha ?? HasAlpha(image);
        if (alpha)
        {
            var png = PreviewArtifactEncoder.EncodePng(image);
            contentType = "image/png";
            return png;
        }

        var jpeg = PreviewArtifactEncoder.EncodeJpeg(image, 85);
        contentType = "image/jpeg";
        return jpeg;
    }

    /// <summary>Scales a decoded image so its longest edge fits the bound, never upscaling.</summary>
    internal static SKImage? Scale(SKImage image, int maxDimension)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= maxDimension)
        {
            return null;
        }

        var scale = (float)maxDimension / longest;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var source = SKBitmap.FromImage(image);
        using var resized = source?.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear));
        return resized is null ? null : SKImage.FromBitmap(resized);
    }

    private static byte[]? Encode(byte[] rgba, int width, int height, bool hasAlpha, out string contentType)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var image = SKImage.FromPixelCopy(info, rgba);
        if (image is null)
        {
            contentType = "image/png";
            return null;
        }

        return EncodeImage(image, out contentType, hasAlpha);
    }

    private static bool HasAlpha(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        if (bitmap is null)
        {
            return false;
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha != 255)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
