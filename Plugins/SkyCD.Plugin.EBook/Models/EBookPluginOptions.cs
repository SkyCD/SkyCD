using System;

namespace SkyCD.Plugin.EBook.Models;

/// <summary>
/// Fixed bounds the plugin applies on top of the host's budgets. The host still owns the authoritative
/// source-read and artifact limits; these values stop the plugin from reading or emitting more than it can
/// use, independently of how the user configured the plugin.
/// </summary>
public sealed record EBookPluginOptions
{
    public EBookPluginOptions(
        long maxSourceBytes = 64L * 1024 * 1024,
        int maxMetadataPartBytes = 2 * 1024 * 1024,
        int maxArchiveEntries = 20_000,
        long maxCoverBytes = 8L * 1024 * 1024,
        int maxCoverPixels = 48_000_000,
        int maxFieldCharacters = 4096,
        int jpegQuality = 85)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataPartBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCoverBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCoverPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFieldCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThan(jpegQuality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jpegQuality, 100);

        MaxSourceBytes = maxSourceBytes;
        MaxMetadataPartBytes = maxMetadataPartBytes;
        MaxArchiveEntries = maxArchiveEntries;
        MaxCoverBytes = maxCoverBytes;
        MaxCoverPixels = maxCoverPixels;
        MaxFieldCharacters = maxFieldCharacters;
        JpegQuality = jpegQuality;
    }

    /// <summary>Whole-source bound for formats that have to be read completely, such as FictionBook XML.</summary>
    public long MaxSourceBytes { get; }

    /// <summary>
    /// Bound for one metadata document — the EPUB container/package parts or the whole book record of a
    /// Palm database. A larger part is skipped rather than parsed.
    /// </summary>
    public int MaxMetadataPartBytes { get; }

    /// <summary>Entry visit cap, so a pathological EPUB cannot spin the scan.</summary>
    public int MaxArchiveEntries { get; }

    /// <summary>An embedded cover larger than this is not turned into a thumbnail; metadata is unaffected.</summary>
    public long MaxCoverBytes { get; }

    /// <summary>
    /// Pixel cap for the cover decoded into a thumbnail. A small compressed file can still describe a huge
    /// canvas, and the decoded bitmap is what consumes memory, so the compressed size alone is not a safe bound.
    /// </summary>
    public int MaxCoverPixels { get; }

    /// <summary>Longest value kept per metadata field, including the description.</summary>
    public int MaxFieldCharacters { get; }

    /// <summary>JPEG quality of the generated cover.</summary>
    public int JpegQuality { get; }

    public static EBookPluginOptions Default { get; } = new();
}
