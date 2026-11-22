using System;

namespace SkyCD.Plugin.Metadata.Video.Models;

/// <summary>
/// Bounds and external-decoder configuration for the video plugin.
/// </summary>
/// <remarks>
/// An external decoder is opt-in and never fetched: <see cref="FFmpegPath"/> is an explicit path, and when it
/// is null the plugin looks at <see cref="FFmpegPathEnvironmentVariable"/> and then at <c>PATH</c>. Nothing is
/// downloaded and no codec pack is assumed, because FFmpeg builds differ in license — a GPL build with
/// <c>--enable-libx264</c> and an LGPL build are not interchangeable, so the operator decides which binary
/// the plugin may run. When no binary is found, poster generation is skipped and metadata-only results are
/// stored, which is the documented fallback rather than an error.
/// </remarks>
public sealed record VideoPluginOptions
{
    /// <summary>Environment variable consulted for an explicit decoder path.</summary>
    public const string FFmpegPathEnvironmentVariable = "SKYCD_FFMPEG_PATH";

    public VideoPluginOptions(
        int headerScanBytes = 4 * 1024 * 1024,
        long maxPosterSourceBytes = 16 * 1024 * 1024,
        int maxPosterDimension = 512,
        double posterTimestampSeconds = 1.0,
        int decoderTimeoutMilliseconds = 15000,
        string? ffmpegPath = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headerScanBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPosterSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPosterDimension);
        ArgumentOutOfRangeException.ThrowIfNegative(decoderTimeoutMilliseconds);

        HeaderScanBytes = headerScanBytes;
        MaxPosterSourceBytes = maxPosterSourceBytes;
        MaxPosterDimension = maxPosterDimension;
        PosterTimestampSeconds = posterTimestampSeconds < 0 ? 0 : posterTimestampSeconds;
        DecoderTimeoutMilliseconds = decoderTimeoutMilliseconds;
        FFmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : ffmpegPath.Trim();
    }

    /// <summary>How far into the file to scan for container headers.</summary>
    public int HeaderScanBytes { get; }

    /// <summary>
    /// Sources larger than this cannot be piped to an external decoder, so they are stored metadata-only.
    /// Aligned with the host's per-provider source budget by default, since that is what bounds the read.
    /// </summary>
    public long MaxPosterSourceBytes { get; }

    /// <summary>Longest edge of a generated poster frame, in pixels.</summary>
    public int MaxPosterDimension { get; }

    /// <summary>
    /// Where in the timeline to take the poster frame. A second in avoids opening titles and black fade-ins
    /// while still being reachable in short clips; sources shorter than this fall back to the first frame.
    /// </summary>
    public double PosterTimestampSeconds { get; }

    /// <summary>Wall-clock limit for one external decoder run, on top of the host's provider timeout.</summary>
    public int DecoderTimeoutMilliseconds { get; }

    /// <summary>Explicit decoder executable, or null to discover one.</summary>
    public string? FFmpegPath { get; }

    public static VideoPluginOptions Default { get; } = new();
}
