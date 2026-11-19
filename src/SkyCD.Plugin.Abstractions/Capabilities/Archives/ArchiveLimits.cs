using System;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// Hard bounds applied while inspecting or extracting archive entries.
/// </summary>
public sealed record ArchiveLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 268_435_456,
    long MaxTotalBytes = 1_073_741_824)
{
    /// <summary>
    /// Gets the default limits: 100,000 entries, 256 MiB per entry and 1 GiB total.
    /// </summary>
    public static ArchiveLimits Default { get; } = new();

    /// <summary>
    /// Throws when any limit is not positive.
    /// </summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalBytes);
    }
}
