using System;
using System.Threading;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Image.Exceptions;
using SkyCD.Plugin.Metadata.Image.Models;

namespace SkyCD.Plugin.Metadata.Image.Parsing;

/// <summary>
/// Decodes images and renders orientation-corrected thumbnails. Every EXIF orientation is expressed as a
/// clockwise rotation plus an optional horizontal mirror, so the display size and the drawing matrix are
/// derived from the same two facts and can never disagree.
/// </summary>
/// <remarks>
/// Input is always an in-memory buffer rather than a stream. Skia pulls bytes from a managed stream through a
/// native callback, and an exception raised there — including the host's own read-budget exception — cannot
/// cross back into managed code and terminates the process instead of failing the provider. Reading first
/// keeps every failure on this plugin's own stack. See <see cref="ImageSourceReader"/>.
/// </remarks>
internal static class ImageDecoder
{
    /// <summary>Animated containers are previewed by their first frame only; the frame count stays in metadata.</summary>
    internal const int PreviewFrameIndex = 0;

    public static ImageProbeInfo Probe(byte[] data)
    {
        using var codec = CreateCodec(data);
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            throw new ImageDecodeException($"the container reports an empty image of {info.Width}x{info.Height}");
        }

        var rotation = RotationDegrees(codec.EncodedOrigin);
        var swapsAxes = rotation is 90 or 270;

        return new ImageProbeInfo(
            info.Width,
            info.Height,
            swapsAxes ? info.Height : info.Width,
            swapsAxes ? info.Width : info.Height,
            NormalizeFormat(codec.EncodedFormat),
            info.AlphaType != SKAlphaType.Opaque,
            (int)codec.EncodedOrigin,
            rotation,
            Math.Max(1, codec.FrameCount));
    }

    /// <summary>
    /// Renders a thumbnail no larger than <paramref name="maxDimension"/> on its longest edge, upright and
    /// with the stored aspect ratio. Images smaller than the bound are never upscaled.
    /// </summary>
    public static ImageThumbnail RenderThumbnail(
        byte[] data,
        ImageProbeInfo probe,
        int maxDimension,
        int jpegQuality,
        CancellationToken cancellationToken)
    {
        using var codec = CreateCodec(data);
        var decodeInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = SKBitmap.Decode(codec, decodeInfo)
                           ?? throw new ImageDecodeException(
                               "the container was recognized but its pixels could not be decoded");

        cancellationToken.ThrowIfCancellationRequested();

        var (targetWidth, targetHeight) = ScaleTo(probe.Width, probe.Height, maxDimension);
        var matrix = BuildMatrix(probe.Orientation, bitmap.Width, bitmap.Height, targetWidth, targetHeight);
        var targetInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(targetInfo)
                            ?? throw new ImageDecodeException(
                                $"a {targetWidth}x{targetHeight} surface could not be allocated");

        // JPEG has no alpha channel, so an opaque backdrop keeps resampled edges from turning black.
        surface.Canvas.Clear(probe.HasAlpha ? SKColors.Transparent : SKColors.White);
        surface.Canvas.SetMatrix(matrix);

        using var image = SKImage.FromBitmap(bitmap);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(
            image, SKPoint.Empty, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

        cancellationToken.ThrowIfCancellationRequested();

        using var snapshot = surface.Snapshot();
        var format = probe.HasAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var encoded = snapshot.Encode(format, probe.HasAlpha ? 100 : jpegQuality)
                            ?? throw new ImageDecodeException("the thumbnail could not be encoded");

        return new ImageThumbnail(
            probe.HasAlpha ? "image/png" : "image/jpeg",
            encoded.ToArray(),
            targetWidth,
            targetHeight);
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

    /// <summary>
    /// Maps stored pixel coordinates onto the oriented, scaled thumbnail. Orientation is decomposed into a
    /// clockwise rotation and an optional horizontal mirror of the stored image.
    /// </summary>
    internal static SKMatrix BuildMatrix(int orientation, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var origin = (SKEncodedOrigin)orientation;
        var rotation = RotationDegrees(origin);
        var mirror = MirrorsHorizontally(origin);
        var swapsAxes = rotation is 90 or 270;
        var orientedWidth = swapsAxes ? sourceHeight : sourceWidth;
        var orientedHeight = swapsAxes ? sourceWidth : sourceHeight;
        var scaleX = (float)targetWidth / orientedWidth;
        var scaleY = (float)targetHeight / orientedHeight;
        var a = sourceWidth;
        var b = sourceHeight;

        // x' = ScaleX*x + SkewX*y + TransX ; y' = SkewY*x + ScaleY*y + TransY
        return (rotation, mirror) switch
        {
            (0, false) => Matrix(scaleX, 0, 0, 0, scaleY, 0),
            (0, true) => Matrix(-scaleX, 0, scaleX * a, 0, scaleY, 0),
            (90, false) => Matrix(0, -scaleX, scaleX * b, scaleY, 0, 0),
            (90, true) => Matrix(0, -scaleX, scaleX * b, -scaleY, 0, scaleY * a),
            (180, false) => Matrix(-scaleX, 0, scaleX * a, 0, -scaleY, scaleY * b),
            (180, true) => Matrix(scaleX, 0, 0, 0, -scaleY, scaleY * b),
            (_, false) => Matrix(0, scaleX, 0, -scaleY, 0, scaleY * a),
            _ => Matrix(0, scaleX, 0, scaleY, 0, 0)
        };
    }

    private static SKCodec CreateCodec(byte[] data)
    {
        using var buffer = SKData.CreateCopy(data);
        return SKCodec.Create(buffer)
               ?? throw new ImageDecodeException("the source is not a recognized image container");
    }

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY)
    {
        var matrix = SKMatrix.CreateIdentity();
        matrix.ScaleX = scaleX;
        matrix.SkewX = skewX;
        matrix.TransX = transX;
        matrix.SkewY = skewY;
        matrix.ScaleY = scaleY;
        matrix.TransY = transY;
        return matrix;
    }

    internal static int RotationDegrees(SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.BottomRight => 180,
        SKEncodedOrigin.LeftTop => 90,
        SKEncodedOrigin.RightTop => 90,
        SKEncodedOrigin.RightBottom => 270,
        SKEncodedOrigin.LeftBottom => 270,
        _ => 0
    };

    internal static bool MirrorsHorizontally(SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.TopRight => true,
        SKEncodedOrigin.BottomLeft => true,
        SKEncodedOrigin.LeftTop => true,
        SKEncodedOrigin.RightBottom => true,
        _ => false
    };

    private static string NormalizeFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "jpeg",
        SKEncodedImageFormat.Png => "png",
        SKEncodedImageFormat.Webp => "webp",
        SKEncodedImageFormat.Gif => "gif",
        SKEncodedImageFormat.Bmp => "bmp",
        SKEncodedImageFormat.Ico => "ico",
        SKEncodedImageFormat.Heif => "heif",
        SKEncodedImageFormat.Avif => "avif",
        _ => format.ToString().ToLowerInvariant()
    };
}

/// <summary>An encoded thumbnail and the dimensions it was rendered at.</summary>
internal sealed record ImageThumbnail(string ContentType, byte[] Data, int Width, int Height);
