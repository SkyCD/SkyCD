using System;
using SkiaSharp;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.AudioCD.Export;

/// <summary>
/// Fits downloaded cover art to a requested longest edge. Images that are already small enough are kept
/// byte-for-byte, so the plugin never upscales and never re-encodes needlessly.
/// </summary>
public sealed class AudioCdCoverRenderer
{
    public MediaPreviewArtifact? Render(byte[] data, string contentType, int longestEdge)
    {
        if (data.Length == 0 || longestEdge <= 0)
        {
            return null;
        }

        using var encodedData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encodedData);
        if (codec is null)
        {
            return null;
        }

        using var original = SKBitmap.Decode(codec);
        if (original is null)
        {
            return null;
        }

        var (width, height) = CoverImageRenderer.ScaleTo(original.Width, original.Height, longestEdge);
        if (width == original.Width && height == original.Height)
        {
            return new MediaPreviewArtifact(contentType, data);
        }

        using var resized = original.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(resized);
        var encoded = PreviewArtifactEncoder.EncodePng(image, 90);
        return encoded is null ? null : new MediaPreviewArtifact("image/png", encoded);
    }
}
