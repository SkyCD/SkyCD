using System;

namespace SkyCD.Plugin.FileFormat.Arc.Models;

/// <summary>Bounds applied while extracting ARC entries for host indexing.</summary>
public sealed record ArcIndexLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 268_435_456,
    long MaxTotalBytes = 1_073_741_824)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static ArcIndexLimits Default { get; } = new();

    /// <summary>Throws when any bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalBytes);
    }
}
