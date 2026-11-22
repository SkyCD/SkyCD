using System;

namespace SkyCD.Plugin.Metadata.Text.Models;

/// <summary>
/// Bounds the text plugin applies on top of the host's own limits.
/// </summary>
public sealed record TextPluginOptions
{
    public TextPluginOptions(
        int maxExcerptCharacters = 8192,
        long maxScanBytes = 16 * 1024 * 1024,
        long maxSourceBytes = 64 * 1024 * 1024,
        int binaryDetectionWindowBytes = 8192,
        double maxBinaryControlRatio = 0.02)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExcerptCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxScanBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binaryDetectionWindowBytes);

        if (maxBinaryControlRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBinaryControlRatio), maxBinaryControlRatio,
                "The control-character ratio must be between 0 and 1.");
        }

        MaxExcerptCharacters = maxExcerptCharacters;
        MaxScanBytes = maxScanBytes;
        MaxSourceBytes = maxSourceBytes;
        BinaryDetectionWindowBytes = binaryDetectionWindowBytes;
        MaxBinaryControlRatio = maxBinaryControlRatio;
    }

    /// <summary>Upper bound of the published excerpt, in characters.</summary>
    public int MaxExcerptCharacters { get; }

    /// <summary>
    /// How many leading bytes are read and analysed. Everything past this bound is counted in
    /// <see cref="TextSchema.ByteLength"/> but never decoded, so a huge log file stays cheap.
    /// </summary>
    public long MaxScanBytes { get; }

    /// <summary>Sources larger than this are rejected before any byte is read.</summary>
    public long MaxSourceBytes { get; }

    /// <summary>How many leading bytes are inspected for binary control characters.</summary>
    public int BinaryDetectionWindowBytes { get; }

    /// <summary>
    /// Fraction of non-text control characters above which a source is treated as binary and rejected rather
    /// than displayed as arbitrary bytes.
    /// </summary>
    public double MaxBinaryControlRatio { get; }

    public static TextPluginOptions Default { get; } = new();
}
