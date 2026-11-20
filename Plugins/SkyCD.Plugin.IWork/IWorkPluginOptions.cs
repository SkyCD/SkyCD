using System;

namespace SkyCD.Plugin.IWork;

/// <summary>
/// Bounds the Apple iWork plugin applies on top of the host's own limits.
/// </summary>
public sealed record IWorkPluginOptions
{
    public IWorkPluginOptions(
        long maxSourceBytes = 64 * 1024 * 1024,
        int maxMetadataBytes = 1024 * 1024,
        int maxIndexBytes = 32 * 1024 * 1024,
        int maxPreviewBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIndexBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPreviewBytes);

        MaxSourceBytes = maxSourceBytes;
        MaxMetadataBytes = maxMetadataBytes;
        MaxIndexBytes = maxIndexBytes;
        MaxPreviewBytes = maxPreviewBytes;
    }

    /// <summary>
    /// Sources larger than this are rejected before any byte is read. Kept above the host's default source
    /// budget so the host limit is what normally binds.
    /// </summary>
    public long MaxSourceBytes { get; }

    /// <summary>Upper bound of a single plain metadata member, such as a property list.</summary>
    public int MaxMetadataBytes { get; }

    /// <summary>Upper bound of a nested <c>Index.zip</c> read in full before it is opened as an archive.</summary>
    public int MaxIndexBytes { get; }

    /// <summary>Upper bound of the thumbnail artifact published by the preview capability.</summary>
    public int MaxPreviewBytes { get; }

    public static IWorkPluginOptions Default { get; } = new();
}
