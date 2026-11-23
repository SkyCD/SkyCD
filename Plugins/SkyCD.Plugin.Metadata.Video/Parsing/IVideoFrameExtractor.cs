using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Produces one poster frame from a video source.
/// </summary>
/// <remarks>
/// Container metadata is always readable in-process, but turning a frame of H.264, HEVC, VP9 or AV1 into an
/// image needs a decoder. The plugin ships one (OpenCV videoio) and keeps an operator-supplied FFmpeg binary
/// as a fallback, but any extractor can still report itself unavailable. This is therefore an optional
/// capability: when no extractor is available the plugin stores metadata only, which is a documented
/// outcome rather than a failure.
/// </remarks>
public interface IVideoFrameExtractor
{
    /// <summary>True when a decoder was explicitly configured or found on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable description of what was found, or why nothing was, for diagnostics.</summary>
    string Describe();

    /// <summary>
    /// Writes the source into <paramref name="pipe"/> and returns one JPEG frame, or null when no frame could
    /// be produced. The plugin owns the byte budget, so only what it writes can be decoded.
    /// </summary>
    Task<byte[]?> ExtractJpegAsync(
        Func<Stream, CancellationToken, Task> writeSourceAsync,
        double timestampSeconds,
        int maxDimension,
        CancellationToken cancellationToken);
}
