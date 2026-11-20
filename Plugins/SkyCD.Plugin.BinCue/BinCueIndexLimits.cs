using System;

namespace SkyCD.Plugin.BinCue;

/// <summary>Bounds applied while listing and extracting BIN/CUE image entries for host indexing.</summary>
public sealed record BinCueIndexLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 268_435_456,
    long MaxTotalBytes = 1_073_741_824,
    int MaxDepth = 128)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static BinCueIndexLimits Default { get; } = new();

    /// <summary>Throws when any bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
    }
}
