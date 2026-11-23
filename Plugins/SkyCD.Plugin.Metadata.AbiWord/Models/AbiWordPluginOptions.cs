using System;

namespace SkyCD.Plugin.Metadata.AbiWord.Models;

public sealed record AbiWordPluginOptions
{
    public AbiWordPluginOptions(
        int maxSourceBytes = 16 * 1024 * 1024,
        int maxDecompressedBytes = 32 * 1024 * 1024,
        int maxMetadataCharacters = 4096,
        int maxThumbnailDimension = 768,
        int maxRenderedCharacters = 20000,
        int maxRenderedParagraphs = 200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedParagraphs);
        MaxSourceBytes = maxSourceBytes;
        MaxDecompressedBytes = maxDecompressedBytes;
        MaxMetadataCharacters = maxMetadataCharacters;
        MaxThumbnailDimension = maxThumbnailDimension;
        MaxRenderedCharacters = maxRenderedCharacters;
        MaxRenderedParagraphs = maxRenderedParagraphs;
    }

    public int MaxSourceBytes { get; }
    public int MaxDecompressedBytes { get; }
    public int MaxMetadataCharacters { get; }

    /// <summary>Longest edge of the rendered page thumbnail, in pixels.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>How many body characters the thumbnail pass may collect and lay out.</summary>
    public int MaxRenderedCharacters { get; }

    /// <summary>How many leading paragraphs the thumbnail pass may walk.</summary>
    public int MaxRenderedParagraphs { get; }

    public static AbiWordPluginOptions Default { get; } = new();
}
