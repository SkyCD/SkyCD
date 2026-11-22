using System;

namespace SkyCD.Plugin.Metadata.Works.Models;

/// <summary>
/// Bounds the Microsoft Works plugin applies on top of the host's own limits.
/// </summary>
public sealed record WorksPluginOptions
{
    public WorksPluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxPropertyStreamBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPropertyStreamBytes);

        MaxSourceBytes = maxSourceBytes;
        MaxPropertyStreamBytes = maxPropertyStreamBytes;
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

    public static WorksPluginOptions Default { get; } = new();
}
