using System;

namespace SkyCD.Plugin.Metadata.WordPerfect.Models;

/// <summary>
/// Bounds the WordPerfect plugin applies on top of the host's own limits.
/// </summary>
public sealed record WordPerfectPluginOptions
{
    public WordPerfectPluginOptions(long maxSourceBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);

        MaxSourceBytes = maxSourceBytes;
    }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    public static WordPerfectPluginOptions Default { get; } = new();
}
