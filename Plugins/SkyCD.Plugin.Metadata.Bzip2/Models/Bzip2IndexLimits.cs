using System;

namespace SkyCD.Plugin.Metadata.Bzip2.Models;

/// <summary>Bounds applied while extracting the bzip2 stream for host indexing.</summary>
public sealed record Bzip2IndexLimits(long MaxEntryBytes = 268_435_456)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static Bzip2IndexLimits Default { get; } = new();

    /// <summary>Throws when the bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
    }
}
