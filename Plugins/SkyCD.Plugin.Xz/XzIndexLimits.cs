using System;

namespace SkyCD.Plugin.Xz;

/// <summary>Bounds applied while extracting the xz stream for host indexing.</summary>
public sealed record XzIndexLimits(long MaxEntryBytes = 268_435_456)
{
    /// <summary>Gets the limits used when none are configured.</summary>
    public static XzIndexLimits Default { get; } = new();

    /// <summary>Throws when the bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
    }
}
