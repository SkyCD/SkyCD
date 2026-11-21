using System;

namespace SkyCD.Plugin.Executable.Models;

/// <summary>
/// Bounds the executable plugin applies on top of the host's own limits. Every value is a hard ceiling, so a
/// crafted executable cannot make the plugin allocate or decode without limit.
/// </summary>
public sealed record ExecutablePluginOptions
{
    /// <summary>The largest edge the encoded icon may have; larger configured values are clamped.</summary>
    public const int MaxIconSize = 128;

    /// <summary>The edge, in pixels, the icon is rendered and shown at when the caller does not choose one.</summary>
    public const int DefaultIconSize = 128;

    /// <summary>
    /// Number of pixels an icon image may have. Icons are at most 256 by 256, so anything above this bound is
    /// either a corrupt header or an attempt to make the decoder allocate a huge raster.
    /// </summary>
    public const int MaximumIconPixels = 256 * 256;

    public ExecutablePluginOptions(
        long maxSourceBytes = 512L * 1024 * 1024,
        long maxIconSourceBytes = 64L * 1024 * 1024,
        int maxIconBytes = 4 * 1024 * 1024,
        int maxIndexedIconImages = 64,
        int maxPngBytes = 1024 * 1024,
        int iconSize = DefaultIconSize,
        int maxMetadataValueCharacters = 512)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIconSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIconBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIndexedIconImages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPngBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iconSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataValueCharacters);

        MaxSourceBytes = maxSourceBytes;
        MaxIconBytes = maxIconBytes;
        MaxIndexedIconImages = maxIndexedIconImages;
        MaxPngBytes = maxPngBytes;
        IconSize = Math.Clamp(iconSize, 16, MaxIconSize);
        MaxMetadataValueCharacters = maxMetadataValueCharacters;

        // An icon is never taken from a source larger than the metadata bound; the smaller limit wins.
        MaxIconSourceBytes = Math.Min(maxIconSourceBytes, maxSourceBytes);
    }

    /// <summary>Sources above this are not parsed by the metadata capability at all.</summary>
    public long MaxSourceBytes { get; }

    /// <summary>
    /// Sources above this keep every metadata value but publish no icon, so indexing a multi-hundred-megabyte
    /// installer never charges its whole size against the pass budget just to find an icon.
    /// </summary>
    public long MaxIconSourceBytes { get; }

    /// <summary>Upper bound of one icon resource taken from the file, both the group and each image.</summary>
    public int MaxIconBytes { get; }

    /// <summary>
    /// Number of icon images inspected while looking for one that decodes. Real executables carry a handful; a
    /// huge or hostile list is truncated instead of decoded exhaustively.
    /// </summary>
    public int MaxIndexedIconImages { get; }

    /// <summary>Upper bound of the encoded PNG preview; a larger result is treated as no icon.</summary>
    public int MaxPngBytes { get; }

    /// <summary>Edge of the encoded PNG; always between 16 and <see cref="MaxIconSize"/> pixels.</summary>
    public int IconSize { get; }

    /// <summary>Upper bound applied to each published text value.</summary>
    public int MaxMetadataValueCharacters { get; }

    public static ExecutablePluginOptions Default { get; } = new();
}
