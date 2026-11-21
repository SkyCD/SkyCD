using System;

namespace SkyCD.Plugin.Image.Models;

/// <summary>
/// Bounds the image plugin applies on top of the host's own limits. The host still owns the authoritative
/// source-read and artifact budgets; these values only stop the plugin from asking for work it cannot use.
/// </summary>
public sealed record ImagePluginOptions
{
    public ImagePluginOptions(
        int maxThumbnailDimension = 512,
        int jpegQuality = 85,
        long maxSourceBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);

        if (jpegQuality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(jpegQuality), jpegQuality,
                "JPEG quality must be between 1 and 100.");
        }

        MaxThumbnailDimension = maxThumbnailDimension;
        JpegQuality = jpegQuality;

        // Kept above the host's default source budget so the host limit is what normally binds, and the
        // plugin never silently disagrees with it.
        MaxSourceBytes = maxSourceBytes;
    }

    /// <summary>Longest edge of a generated thumbnail, in pixels.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>Encoder quality for opaque thumbnails, 1 to 100.</summary>
    public int JpegQuality { get; }

    /// <summary>Sources larger than this are rejected before any byte is read.</summary>
    public long MaxSourceBytes { get; }

    public static ImagePluginOptions Default { get; } = new();
}
