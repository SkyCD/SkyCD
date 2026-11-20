using System;

namespace SkyCD.Plugin.OpenOffice;

/// <summary>
/// Bounds the OpenOffice plugin applies on top of the host's own limits.
/// </summary>
public sealed record OpenOfficePluginOptions
{
    public OpenOfficePluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxMetadataBytes = 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataBytes);

        MaxSourceBytes = maxSourceBytes;
        MaxMetadataBytes = maxMetadataBytes;
    }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>
    /// Upper bound of a single XML member such as <c>meta.xml</c> or the package manifest, measured on the
    /// decompressed bytes actually read rather than on the size the container declares.
    /// </summary>
    public int MaxMetadataBytes { get; }

    public static OpenOfficePluginOptions Default { get; } = new();
}
