using System;

namespace SkyCD.Plugin.Qcow2.Models;

/// <summary>Bounds applied while enumerating and extracting files from QCOW2 images for host indexing.</summary>
public sealed record Qcow2IndexLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 268_435_456,
    long MaxTotalBytes = 1_073_741_824,
    int MaxDepth = 256)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static Qcow2IndexLimits Default { get; } = new();

    /// <summary>Throws when any bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
    }
}
