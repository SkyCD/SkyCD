using System;

namespace SkyCD.Plugin.Metadata.OpenOffice.Models;

/// <summary>
/// Bounds the OpenOffice plugin applies on top of the host's own limits.
/// </summary>
public sealed record OpenOfficePluginOptions
{
    public OpenOfficePluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxMetadataBytes = 1024 * 1024,
        int maxPreviewBytes = 4 * 1024 * 1024,
        int maxPreviewDimension = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPreviewBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPreviewDimension);

        MaxSourceBytes = maxSourceBytes;
        MaxMetadataBytes = maxMetadataBytes;
        MaxPreviewBytes = maxPreviewBytes;
        MaxPreviewDimension = maxPreviewDimension;
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

    /// <summary>
    /// An embedded thumbnail larger than this is treated as absent, measured on the decompressed bytes
    /// actually read rather than on the size the container declares.
    /// </summary>
    public int MaxPreviewBytes { get; }

    /// <summary>
    /// Longest edge of an embedded thumbnail, in pixels. Real previews are far smaller, so a larger value only
    /// appears in a crafted document and is refused before the host decodes it.
    /// </summary>
    public int MaxPreviewDimension { get; }

    public static OpenOfficePluginOptions Default { get; } = new();
}
