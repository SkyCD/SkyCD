using System;

namespace SkyCD.Plugin.FileFormat.Nrg.Models;

/// <summary>Bounds applied while listing and extracting NRG entries for host indexing.</summary>
public sealed record NrgIndexLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 268_435_456)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static NrgIndexLimits Default { get; } = new();

    /// <summary>Throws when any bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
    }
}
