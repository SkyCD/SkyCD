using System;

namespace SkyCD.Plugin.FileFormat.Gzip.Models;

/// <summary>Bounds applied while extracting the gzip member for host indexing.</summary>
public sealed record GzipIndexLimits(long MaxEntryBytes = 268_435_456)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static GzipIndexLimits Default { get; } = new();

    /// <summary>Throws when the bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
    }
}
