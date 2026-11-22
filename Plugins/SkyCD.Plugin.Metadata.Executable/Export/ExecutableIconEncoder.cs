using System;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Export;

/// <summary>
/// Scales a decoded icon down to the preview edge and encodes it as PNG.
/// </summary>
/// <remarks>
/// The icon is averaged in premultiplied alpha and keeps its transparency, so the catalog can show the
/// program's icon over whatever background the item row has instead of a rectangular plate around its
/// silhouette. Averaging premultiplied values also keeps the fully transparent margin of an icon from
/// dragging its edge colors toward black.
/// </remarks>
internal static class ExecutableIconEncoder
{
    /// <summary>
    /// Scales <paramref name="image"/> to a square of <paramref name="size"/> pixels and encodes it as PNG.
    /// Null when the image carries no visible pixels; an oversized result raises
    /// <see cref="ExecutableIconException"/> so the caller can answer with "no icon".
    /// </summary>
    public static byte[]? TryEncode(ExecutableIconImage image, int size, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (size > ExecutablePluginOptions.MaxIconSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (!image.HasVisiblePixels)
        {
            return null;
        }

        var flattened = Flatten(image, size);
        var png = EncodePng(flattened);
        if (png.Length > maximumBytes)
        {
            throw new ExecutableIconException(
                $"the encoded icon is {png.Length} bytes, above the {maximumBytes} byte bound");
        }

        return png;
    }

    /// <summary>
    /// Draws the icon onto a transparent <paramref name="size"/> square. An icon that fits keeps its own pixels
    /// and is centred, so a small icon is never stretched; a larger one is box-filtered down, with its longer
    /// edge fitted to the square, so no detail is invented and no pixel is sampled twice.
    /// </summary>
    public static ExecutableIconImage Flatten(ExecutableIconImage image, int size)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var source = image.Rgba;
        var sourceWidth = image.Width;
        var sourceHeight = image.Height;
        var output = new byte[size * size * 4];

        if (sourceWidth <= size && sourceHeight <= size)
        {
            var offsetX = (size - sourceWidth) / 2;
            var offsetY = (size - sourceHeight) / 2;
            for (var row = 0; row < sourceHeight; row++)
            {
                source.AsSpan(row * sourceWidth * 4, sourceWidth * 4)
                    .CopyTo(output.AsSpan((((row + offsetY) * size) + offsetX) * 4, sourceWidth * 4));
            }

            return new ExecutableIconImage(size, size, output);
        }

        var scale = Math.Max((double)sourceWidth / size, (double)sourceHeight / size);
        var width = Math.Clamp((int)Math.Round(sourceWidth / scale), 1, size);
        var height = Math.Clamp((int)Math.Round(sourceHeight / scale), 1, size);
        var left0 = (size - width) / 2;
        var top0 = (size - height) / 2;
        for (var y = 0; y < height; y++)
        {
            var top = y * sourceHeight / height;
            var bottom = Math.Max(top + 1, (y + 1) * sourceHeight / height);
            for (var x = 0; x < width; x++)
            {
                var left = x * sourceWidth / width;
                var right = Math.Max(left + 1, (x + 1) * sourceWidth / width);

                var alphaSum = 0.0;
                var redSum = 0.0;
                var greenSum = 0.0;
                var blueSum = 0.0;
                var samples = 0;
                for (var sourceY = top; sourceY < bottom; sourceY++)
                {
                    for (var sourceX = left; sourceX < right; sourceX++)
                    {
                        var offset = ((sourceY * sourceWidth) + sourceX) * 4;
                        var alpha = source[offset + 3] / 255.0;
                        redSum += source[offset] * alpha;
                        greenSum += source[offset + 1] * alpha;
                        blueSum += source[offset + 2] * alpha;
                        alphaSum += alpha;
                        samples++;
                    }
                }

                var target = (((y + top0) * size) + x + left0) * 4;
                output[target] = Unpremultiply(redSum, alphaSum);
                output[target + 1] = Unpremultiply(greenSum, alphaSum);
                output[target + 2] = Unpremultiply(blueSum, alphaSum);
                output[target + 3] = (byte)Math.Clamp(Math.Round(alphaSum / samples * 255.0), 0, 255);
            }
        }

        return new ExecutableIconImage(size, size, output);
    }

    private static byte Unpremultiply(double premultipliedSum, double alphaSum) =>
        alphaSum <= 0 ? (byte)0 : (byte)Math.Clamp(Math.Round(premultipliedSum / alphaSum), 0, 255);

    /// <summary>Encodes the straight RGBA raster as an 8-bit PNG; Skia stores it straight, as PNG requires.</summary>
    private static byte[] EncodePng(ExecutableIconImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        var target = bitmap.GetPixelSpan();
        for (var row = 0; row < image.Height; row++)
        {
            var source = image.Rgba.AsSpan(row * image.Width * 4, image.Width * 4);
            var destination = target.Slice(row * bitmap.RowBytes, image.Width * 4);
            for (var column = 0; column < source.Length; column += 4)
            {
                var alpha = source[column + 3];
                destination[column] = Premultiply(source[column], alpha);
                destination[column + 1] = Premultiply(source[column + 1], alpha);
                destination[column + 2] = Premultiply(source[column + 2], alpha);
                destination[column + 3] = alpha;
            }
        }

        using var imageHandle = SKImage.FromBitmap(bitmap);
        using var encoded = imageHandle.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new ExecutableIconException("the encoded icon PNG is not available");
        return encoded.ToArray();
    }

    private static byte Premultiply(byte value, byte alpha) => (byte)(((value * alpha) + 127) / 255);
}
