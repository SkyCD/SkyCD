using System;

namespace SkyCD.Plugin.Pdf.Models;

/// <summary>
/// Bounds the PDF plugin applies on top of the host's own limits.
/// </summary>
public sealed record PdfPluginOptions
{
    public PdfPluginOptions(
        int maxThumbnailDimension = 768,
        int maxTextExcerptCharacters = 4096,
        int maxTextExcerptPages = 5,
        long maxSourceBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextExcerptCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextExcerptPages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);

        MaxThumbnailDimension = maxThumbnailDimension;
        MaxTextExcerptCharacters = maxTextExcerptCharacters;
        MaxTextExcerptPages = maxTextExcerptPages;
        MaxSourceBytes = maxSourceBytes;
    }

    /// <summary>Longest edge of the rendered first page, in pixels.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>Upper bound of the plain-text excerpt published through the metadata schema.</summary>
    public int MaxTextExcerptCharacters { get; }

    /// <summary>How many leading pages the excerpt may draw from before the bound is hit.</summary>
    public int MaxTextExcerptPages { get; }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    public static PdfPluginOptions Default { get; } = new();
}
