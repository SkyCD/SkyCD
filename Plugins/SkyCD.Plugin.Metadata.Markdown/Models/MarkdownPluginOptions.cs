using System;

namespace SkyCD.Plugin.Metadata.Markdown.Models;

/// <summary>
/// Bounds the Markdown plugin applies on top of the host's own limits.
/// </summary>
public sealed record MarkdownPluginOptions
{
    public MarkdownPluginOptions(
        int maxTextExcerptCharacters = 4096,
        int maxMetadataValueCharacters = 2048,
        int maxFrontMatterCharacters = 64 * 1024,
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxThumbnailDimension = 768,
        int maxRenderedCharacters = 6000,
        int maxRenderedBlocks = 200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextExcerptCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataValueCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrontMatterCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedBlocks);

        MaxTextExcerptCharacters = maxTextExcerptCharacters;
        MaxMetadataValueCharacters = maxMetadataValueCharacters;
        MaxFrontMatterCharacters = maxFrontMatterCharacters;
        MaxSourceBytes = maxSourceBytes;
        MaxThumbnailDimension = maxThumbnailDimension;
        MaxRenderedCharacters = maxRenderedCharacters;
        MaxRenderedBlocks = maxRenderedBlocks;
    }

    /// <summary>Upper bound of the plain-text excerpt published through the metadata schema.</summary>
    public int MaxTextExcerptCharacters { get; }

    /// <summary>Upper bound applied to each value read from the front matter.</summary>
    public int MaxMetadataValueCharacters { get; }

    /// <summary>Front matter larger than this is treated as damaged and its fields are not published.</summary>
    public int MaxFrontMatterCharacters { get; }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>Page width in pixels of the rendered thumbnail; the height follows the A-series aspect ratio.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>Upper bound of the characters drawn on the rendered page.</summary>
    public int MaxRenderedCharacters { get; }

    /// <summary>Upper bound of the top-level blocks the rendered page draws.</summary>
    public int MaxRenderedBlocks { get; }

    public static MarkdownPluginOptions Default { get; } = new();
}
