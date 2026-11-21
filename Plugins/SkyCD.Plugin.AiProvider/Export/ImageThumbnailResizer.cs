using System;
using SkiaSharp;
using SkyCD.Plugin.AiProvider.Exceptions;

namespace SkyCD.Plugin.AiProvider.Export;

/// <summary>Bounds cached preview images before they are sent to a provider.</summary>
public static class ImageThumbnailResizer
{
    public const int MaximumDimension = 448;

    /// <summary>
    /// Returns the original bytes when the image already fits; otherwise a PNG re-encoded with its longest
    /// edge limited to <see cref="MaximumDimension"/> and its aspect ratio preserved.
    /// </summary>
    public static (string ContentType, byte[] Data) ResizeIfNeeded(byte[] data, string contentType)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0 || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return (contentType, data);
        }

        using var bitmap = Decode(data);

        if (bitmap.Width <= MaximumDimension && bitmap.Height <= MaximumDimension)
        {
            return (contentType, data);
        }

        var scale = Math.Min((double)MaximumDimension / bitmap.Width, (double)MaximumDimension / bitmap.Height);
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var targetInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(targetInfo)
                            ?? throw new AiProviderException("the resized image could not be allocated");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale((float)width / bitmap.Width, (float)height / bitmap.Height);

        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(image, SKPoint.Empty,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new AiProviderException("the resized image could not be encoded");
        return ("image/png", encoded.ToArray());
    }

    private static SKBitmap Decode(byte[] data)
    {
        try
        {
            return SKBitmap.Decode(data)
                   ?? throw new AiProviderException("a cached image preview could not be decoded");
        }
        catch (ArgumentException)
        {
            throw new AiProviderException("a cached image preview could not be decoded");
        }
    }
}
