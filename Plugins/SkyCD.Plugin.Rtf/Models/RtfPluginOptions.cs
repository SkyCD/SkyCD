using System;

namespace SkyCD.Plugin.Rtf.Models;

/// <summary>
/// Bounds the RTF plugin applies on top of the host's own limits.
/// </summary>
public sealed record RtfPluginOptions
{
    public RtfPluginOptions(
        int maxThumbnailDimension = 768,
        int maxTextExcerptCharacters = 4096,
        int maxRenderedCharacters = 20000,
        long maxSourceBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextExcerptCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);

        MaxThumbnailDimension = maxThumbnailDimension;
        MaxTextExcerptCharacters = maxTextExcerptCharacters;
        MaxRenderedCharacters = maxRenderedCharacters;
        MaxSourceBytes = maxSourceBytes;
    }

    /// <summary>Longest edge of the rendered thumbnail, in pixels.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>Upper bound of the plain-text excerpt published through the metadata schema.</summary>
    public int MaxTextExcerptCharacters { get; }

    /// <summary>How many document characters the thumbnail renderer may lay out.</summary>
    public int MaxRenderedCharacters { get; }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    public static RtfPluginOptions Default { get; } = new();
}
