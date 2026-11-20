using SkiaSharp;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AudioCd;

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

        var longest = System.Math.Max(original.Width, original.Height);
        if (longest <= longestEdge)
        {
            return new MediaPreviewArtifact(contentType, data);
        }

        var scale = (double)longestEdge / longest;
        var width = System.Math.Max(1, (int)System.Math.Round(original.Width * scale));
        var height = System.Math.Max(1, (int)System.Math.Round(original.Height * scale));

        using var resized = original.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return encoded is null ? null : new MediaPreviewArtifact("image/png", encoded.ToArray());
    }
}
