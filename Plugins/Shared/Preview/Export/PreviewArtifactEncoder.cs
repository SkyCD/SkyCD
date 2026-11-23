using SkiaSharp;

namespace SkyCD.Plugin.Preview.Export;

/// <summary>One place for the Skia encode call used by plugin previews.</summary>
/// <remarks>
/// Every preview plugin encodes through these two helpers so format and default quality stay consistent.
/// Returns <c>null</c> when Skia cannot produce bytes; callers keep their own failure (null or exception).
/// </remarks>
internal static class PreviewArtifactEncoder
{
    /// <summary>Encodes the image as PNG at the default quality of 100.</summary>
    internal static byte[]? EncodePng(SKImage image, int quality = 100) =>
        image.Encode(SKEncodedImageFormat.Png, quality)?.ToArray();

    /// <summary>Encodes the image as JPEG at the given quality; JPEG has no alpha channel.</summary>
    internal static byte[]? EncodeJpeg(SKImage image, int quality) =>
        image.Encode(SKEncodedImageFormat.Jpeg, quality)?.ToArray();
}
