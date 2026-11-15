namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Bounds applied when the catalog browser displays a cached result. They keep an oversized artifact or a
/// huge text body from being decoded at all, so navigation stays responsive while the media is offline.
/// </summary>
/// <param name="MaxDecodedArtifactBytes">Stored artifacts above this size are reported instead of decoded.</param>
/// <param name="MaxTextPreviewCharacters">Upper bound of a decoded text preview.</param>
/// <param name="ImageCacheEntries">Decoded images kept before the least recently used one is released.</param>
/// <param name="ImageCacheBytes">Decoded pixel budget across all cached images.</param>
public sealed record MediaPreviewDisplayPolicy(
    long MaxDecodedArtifactBytes = 8 * 1024 * 1024,
    int MaxTextPreviewCharacters = 8192,
    int ImageCacheEntries = 64,
    long ImageCacheBytes = 48 * 1024 * 1024)
{
    /// <summary>Approximate decoded size of one image, used to enforce <see cref="ImageCacheBytes"/>.</summary>
    public static long EstimateDecodedImageBytes(int pixelWidth, int pixelHeight) =>
        (long)pixelWidth * pixelHeight * 4;
}
