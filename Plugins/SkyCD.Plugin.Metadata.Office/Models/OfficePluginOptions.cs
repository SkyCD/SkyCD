using System;

namespace SkyCD.Plugin.Metadata.Office.Models;

/// <summary>
/// Bounds the Microsoft Office plugin applies on top of the host's own limits.
/// </summary>
public sealed record OfficePluginOptions
{
    public OfficePluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxMetadataPartBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataPartBytes);

        MaxSourceBytes = maxSourceBytes;
        MaxMetadataPartBytes = maxMetadataPartBytes;
    }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>
    /// A metadata XML part larger than this is treated as absent, because a real part is far smaller and the
    /// bound keeps a corrupted size from costing memory.
    /// </summary>
    public int MaxMetadataPartBytes { get; }

    public static OfficePluginOptions Default { get; } = new();
}
