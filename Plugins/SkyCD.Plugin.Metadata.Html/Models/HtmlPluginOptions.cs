using System;

namespace SkyCD.Plugin.Metadata.Html.Models;

/// <summary>
/// Bounds the HTML plugin applies on top of the host's own limits.
/// </summary>
public sealed record HtmlPluginOptions
{
    public HtmlPluginOptions(
        int maxTextExcerptCharacters = 4096,
        int maxMetadataValueCharacters = 2048,
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxThumbnailDimension = 768,
        int maxRenderedCharacters = 6000,
        int maxRenderedBlocks = 200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextExcerptCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataValueCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedBlocks);

        MaxTextExcerptCharacters = maxTextExcerptCharacters;
        MaxMetadataValueCharacters = maxMetadataValueCharacters;
        MaxSourceBytes = maxSourceBytes;
        MaxThumbnailDimension = maxThumbnailDimension;
        MaxRenderedCharacters = maxRenderedCharacters;
        MaxRenderedBlocks = maxRenderedBlocks;
    }

    /// <summary>Upper bound of the plain-text excerpt published through the metadata schema.</summary>
    public int MaxTextExcerptCharacters { get; }

    /// <summary>Upper bound applied to each value read from the document head.</summary>
    public int MaxMetadataValueCharacters { get; }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>Page width in pixels of the rendered thumbnail; the height follows the A-series aspect ratio.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>Upper bound of the characters drawn on the rendered page.</summary>
    public int MaxRenderedCharacters { get; }

    /// <summary>Upper bound of the document blocks the rendered page draws.</summary>
    public int MaxRenderedBlocks { get; }

    public static HtmlPluginOptions Default { get; } = new();
}
