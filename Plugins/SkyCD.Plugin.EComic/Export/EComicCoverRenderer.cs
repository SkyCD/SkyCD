using System;
using System.Threading;
using SkiaSharp;
using SkyCD.Plugin.EComic.Exceptions;
using SkyCD.Plugin.EComic.Models;

namespace SkyCD.Plugin.EComic.Export;

/// <summary>
/// Turns an encoded comic page into an opaque JPEG cover whose longest edge is at most the configured size.
/// </summary>
/// <remarks>
/// Decoding happens on an in-memory buffer, never on a stream that calls back into the host, so a read
/// failure stays an ordinary managed exception. Pages smaller than the bound keep their size — a cover is
/// never upscaled — and the decoded pixel count is checked before allocation because a small compressed file
/// can still describe a canvas far larger than the cover needs.
/// </remarks>
internal static class EComicCoverRenderer
{
    public static byte[] Render(byte[] page, int coverSize, EComicPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coverSize);
        ArgumentNullException.ThrowIfNull(options);

        using var data = SKData.CreateCopy(page);
        using var codec = SKCodec.Create(data)
                          ?? throw new EComicCoverException("the page is not a recognized image container");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new EComicCoverException($"the page reports an empty image of {info.Width}x{info.Height}");
        }

        if ((long)info.Width * info.Height > options.MaxPagePixels)
        {
            throw new EComicCoverException(
                $"the page decodes to {info.Width}x{info.Height}, above the pixel bound");
        }

        var decodeInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = SKBitmap.Decode(codec, decodeInfo)
                           ?? throw new EComicCoverException("the page pixels could not be decoded");
        cancellationToken.ThrowIfCancellationRequested();

        var (width, height) = ScaleTo(bitmap.Width, bitmap.Height, coverSize);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
                            ?? throw new EComicCoverException($"a {width}x{height} surface could not be allocated");

        // JPEG has no alpha channel; an opaque backdrop keeps edges from being resampled against black.
        surface.Canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(
            image,
            SKRect.Create(0, 0, width, height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint);
        cancellationToken.ThrowIfCancellationRequested();

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, options.JpegQuality)
                            ?? throw new EComicCoverException("the cover could not be encoded");
        return encoded.ToArray();
    }

    /// <summary>Uniform downscale that never upscales and never drops an edge to zero pixels.</summary>
    internal static (int Width, int Height) ScaleTo(int width, int height, int maxDimension)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxDimension)
        {
            return (width, height);
        }

        var scale = (double)maxDimension / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
