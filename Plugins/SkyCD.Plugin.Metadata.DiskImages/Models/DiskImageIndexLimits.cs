using System;

namespace SkyCD.Plugin.Metadata.DiskImages.Models;

/// <summary>Bounds applied while listing and extracting disk-image entries for host indexing.</summary>
/// <remarks>
/// One shared limit type covers every disk-image format. <see cref="MaxDepth"/> is optional so that a
/// format can keep its historical default when the host does not configure one: optical-disc images
/// default to 128 levels and file-system images to 256. ISO 9660/UDF and NRG readers do not apply the
/// total-byte budget, matching their previous behavior.
/// </remarks>
public sealed record DiskImageIndexLimits(
    int MaxEntries = 100_000,
    long MaxEntryBytes = 268_435_456,
    long MaxTotalBytes = 1_073_741_824,
    int? MaxDepth = null)
{
    /// <summary>Gets the nesting limit used by optical-disc formats when <see cref="MaxDepth"/> is not set.</summary>
    public const int OpticalMaxDepth = 128;

    /// <summary>Gets the nesting limit used by file-system image formats when <see cref="MaxDepth"/> is not set.</summary>
    public const int FileSystemMaxDepth = 256;

    /// <summary>Gets the limits used when none are configured.</summary>
    public static DiskImageIndexLimits Default { get; } = new();

    /// <summary>Throws when any configured bound is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalBytes);
        if (MaxDepth is { } depth)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        }
    }
}
