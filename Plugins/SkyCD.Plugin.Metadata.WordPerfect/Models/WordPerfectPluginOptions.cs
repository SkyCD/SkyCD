using System;

namespace SkyCD.Plugin.Metadata.WordPerfect.Models;

/// <summary>
/// Bounds the WordPerfect plugin applies on top of the host's own limits.
/// </summary>
public sealed record WordPerfectPluginOptions
{
    public WordPerfectPluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxThumbnailDimension = 768,
        int maxRenderedCharacters = 20000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedCharacters);

        MaxSourceBytes = maxSourceBytes;
        MaxThumbnailDimension = maxThumbnailDimension;
        MaxRenderedCharacters = maxRenderedCharacters;
    }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>Longest edge of the rendered page thumbnail, in pixels.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>How many body characters the thumbnail pass may collect and lay out.</summary>
    public int MaxRenderedCharacters { get; }

    public static WordPerfectPluginOptions Default { get; } = new();
}
