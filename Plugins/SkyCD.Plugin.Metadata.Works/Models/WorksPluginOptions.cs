using System;

namespace SkyCD.Plugin.Metadata.Works.Models;

/// <summary>
/// Bounds the Microsoft Works plugin applies on top of the host's own limits.
/// </summary>
public sealed record WorksPluginOptions
{
    public WorksPluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxPropertyStreamBytes = 4 * 1024 * 1024,
        int maxTextBodyBytes = 8 * 1024 * 1024,
        int maxTextCharacters = 20000,
        int maxThumbnailDimension = 768,
        int maxEmbeddedImageBytes = 8 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPropertyStreamBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmbeddedImageBytes);

        MaxSourceBytes = maxSourceBytes;
        MaxPropertyStreamBytes = maxPropertyStreamBytes;
        MaxTextBodyBytes = maxTextBodyBytes;
        MaxTextCharacters = maxTextCharacters;
        MaxThumbnailDimension = maxThumbnailDimension;
        MaxEmbeddedImageBytes = maxEmbeddedImageBytes;
    }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>
    /// A summary information stream larger than this is treated as absent, because a real document summary is
    /// far smaller and the bound keeps a corrupted size from costing memory.
    /// </summary>
    public int MaxPropertyStreamBytes { get; }

    /// <summary>
    /// A document text stream larger than this is treated as impossible to preview, so a corrupted size
    /// cannot make the plugin materialize an unbounded body.
    /// </summary>
    public int MaxTextBodyBytes { get; }

    /// <summary>How many leading body characters the text sample and the renderer may use.</summary>
    public int MaxTextCharacters { get; }

    /// <summary>Longest edge of the rendered thumbnail, in pixels.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>An embedded preview image larger than this is ignored.</summary>
    public int MaxEmbeddedImageBytes { get; }

    public static WorksPluginOptions Default { get; } = new();
}
