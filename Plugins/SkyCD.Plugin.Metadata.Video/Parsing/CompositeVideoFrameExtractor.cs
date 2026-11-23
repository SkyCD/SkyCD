using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Video.Models;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Tries the bundled in-process decoder first and falls back to an operator-supplied FFmpeg binary.
/// </summary>
/// <remarks>
/// OpenCV's bundled backend covers the codecs these containers normally carry, but an operator's FFmpeg may
/// still be the only decoder for an unusual codec, and it is also the only poster source on platforms where
/// the Windows native runtime cannot load. The fallback therefore runs exactly when the primary produced no
/// frame, and is skipped entirely when the primary succeeded. A source the primary cannot open never turns
/// into a provider failure: the composite returns no frame when the fallback also has nothing to offer.
/// </remarks>
public sealed class CompositeVideoFrameExtractor : IVideoFrameExtractor
{
    private readonly IVideoFrameExtractor primary;
    private readonly IVideoFrameExtractor? fallback;

    public CompositeVideoFrameExtractor(VideoPluginOptions? options = null)
        : this(new OpenCvFrameExtractor(options), new FFmpegFrameExtractor(options))
    {
    }

    /// <summary>Hosts and tests may name both decoders explicitly; the fallback is optional.</summary>
    public CompositeVideoFrameExtractor(IVideoFrameExtractor primary, IVideoFrameExtractor? fallback = null)
    {
        this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
        this.fallback = fallback;
    }

    public bool IsAvailable => primary.IsAvailable || fallback?.IsAvailable == true;

    public string Describe() => fallback is null
        ? primary.Describe()
        : $"{primary.Describe()} {fallback.Describe()}";

    public async Task<byte[]?> ExtractJpegAsync(
        Func<Stream, CancellationToken, Task> writeSourceAsync,
        double timestampSeconds,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeSourceAsync);

        if (primary.IsAvailable)
        {
            try
            {
                var frame = await primary
                    .ExtractJpegAsync(writeSourceAsync, timestampSeconds, maxDimension, cancellationToken)
                    .ConfigureAwait(false);
                if (frame is { Length: > 0 })
                {
                    return frame;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The primary is optional; any failure is the fallback's turn.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return fallback is { IsAvailable: true }
            ? await fallback
                .ExtractJpegAsync(writeSourceAsync, timestampSeconds, maxDimension, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }
}
