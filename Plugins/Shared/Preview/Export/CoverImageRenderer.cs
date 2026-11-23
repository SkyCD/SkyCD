using System;
using System.Threading;
using SkiaSharp;

namespace SkyCD.Plugin.Preview.Export;

/// <summary>
/// Turns an encoded source image into an opaque JPEG whose longest edge is at most the configured size.
/// </summary>
/// <remarks>
/// Decoding happens on an in-memory buffer, never on a stream that calls back into the host, so a read
/// failure stays an ordinary managed exception. Images smaller than the bound keep their size — a cover is
/// never upscaled — and the decoded pixel count is checked before allocation because a small compressed file
/// can still describe a canvas far larger than the cover needs. The <c>subject</c> parameter is the leading
/// noun phrase of every message ("the cover" or "the page") so each plugin keeps its own wording, and
/// <paramref name="createException"/> lets the caller raise its own exception type.
/// </remarks>
internal static class CoverImageRenderer
{
    internal static byte[] Render(
        byte[] source,
        int maximumDimension,
        long maximumPixels,
        int jpegQuality,
        string subject,
        Func<string, Exception> createException,
        CancellationToken cancellationToken)
    {
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data)
                          ?? throw createException($"{subject} is not a recognized image container");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw createException($"{subject} reports an empty image of {info.Width}x{info.Height}");
        }

        if ((long)info.Width * info.Height > maximumPixels)
        {
            throw createException($"{subject} decodes to {info.Width}x{info.Height}, above the pixel bound");
        }

        var decodeInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = SKBitmap.Decode(codec, decodeInfo)
                           ?? throw createException($"{subject} pixels could not be decoded");
        cancellationToken.ThrowIfCancellationRequested();

        var (width, height) = ScaleTo(bitmap.Width, bitmap.Height, maximumDimension);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
                            ?? throw createException($"a {width}x{height} surface could not be allocated");

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
        return PreviewArtifactEncoder.EncodeJpeg(snapshot, jpegQuality)
               ?? throw createException($"{subject} could not be encoded");
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
