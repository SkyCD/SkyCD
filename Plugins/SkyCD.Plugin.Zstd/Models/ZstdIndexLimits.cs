using System;

namespace SkyCD.Plugin.Zstd.Models;

/// <summary>Bounds applied while extracting the Zstandard stream for host indexing.</summary>
public sealed record ZstdIndexLimits(long MaxEntryBytes = 268_435_456)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static ZstdIndexLimits Default { get; } = new();

    /// <summary>Throws when the bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
    }
}
