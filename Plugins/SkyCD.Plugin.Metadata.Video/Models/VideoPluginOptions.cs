using System;

namespace SkyCD.Plugin.Metadata.Video.Models;

/// <summary>
/// Bounds and decoder configuration for the video plugin.
/// </summary>
/// <remarks>
/// Poster frames decode in-process through the OpenCV videoio runtime that ships with the plugin. An external
/// FFmpeg decoder is optional and never fetched: <see cref="FFmpegPath"/> is an explicit path, and when it is
/// null the plugin looks at <see cref="FFmpegPathEnvironmentVariable"/> and then at <c>PATH</c>. Nothing is
/// downloaded and no external codec pack is assumed; the fallback exists because FFmpeg builds differ in
/// license — a GPL build with <c>--enable-libx264</c> and an LGPL build are not interchangeable — so which
/// binary the plugin may run is the operator's decision. When no fallback is found and the bundled decoder
/// cannot read the source, poster generation is skipped and metadata-only results are stored, which is the
/// documented outcome rather than an error.
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
    /// Sources larger than this are not handed to a decoder, so they are stored metadata-only.
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

    /// <summary>Wall-clock limit for one decoder run, on top of the host's provider timeout.</summary>
    public int DecoderTimeoutMilliseconds { get; }

    /// <summary>Explicit fallback decoder executable, or null to discover one.</summary>
    public string? FFmpegPath { get; }

    public static VideoPluginOptions Default { get; } = new();
}
