using System;

namespace SkyCD.Plugin.EComic.Models;

/// <summary>
/// Fixed bounds the plugin applies on top of the host's budgets. The host still owns the authoritative
/// source-read and artifact limits; these values stop the plugin from reading or emitting more than it can
/// use, independently of how the user configured the plugin.
/// </summary>
public sealed record EComicPluginOptions
{
    public EComicPluginOptions(
        int maxComicInfoBytes = 2 * 1024 * 1024,
        long maxPageBytes = 8 * 1024 * 1024,
        int maxPagePixels = 48_000_000,
        int maxArchiveEntries = 100_000,
        int maxFieldCharacters = 4096,
        int jpegQuality = 85)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxComicInfoBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPagePixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFieldCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThan(jpegQuality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jpegQuality, 100);

        MaxComicInfoBytes = maxComicInfoBytes;
        MaxPageBytes = maxPageBytes;
        MaxPagePixels = maxPagePixels;
        MaxArchiveEntries = maxArchiveEntries;
        MaxFieldCharacters = maxFieldCharacters;
        JpegQuality = jpegQuality;
    }

    /// <summary>ComicInfo.xml larger than this is ignored rather than parsed.</summary>
    public int MaxComicInfoBytes { get; }

    /// <summary>A first page larger than this is not compressed into a cover; the archive still yields metadata.</summary>
    public long MaxPageBytes { get; }

    /// <summary>
    /// Pixel cap for the page decoded into a cover. A small file can still describe a huge canvas, and the
    /// decoded bitmap is what consumes memory, so the compressed size alone is not a safe bound.
    /// </summary>
    public int MaxPagePixels { get; }

    /// <summary>Entry visit cap, so a pathological or damaged archive cannot spin the scan.</summary>
    public int MaxArchiveEntries { get; }

    /// <summary>Longest value kept per XML field, including the summary.</summary>
    public int MaxFieldCharacters { get; }

    /// <summary>JPEG quality of the generated cover.</summary>
    public int JpegQuality { get; }

    public static EComicPluginOptions Default { get; } = new();
}
