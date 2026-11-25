using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Video.Models;
using SkyCD.Plugin.Metadata.Video.Parsing;

namespace SkyCD.Plugin.Metadata.Video;

/// <summary>
/// Produces one bounded poster frame per video, when a decoder is available.
/// </summary>
/// <remarks>
/// Frames are decoded in-process by the OpenCV videoio backend that ships with the plugin, so a fresh install
/// stores a poster without any external tool. An operator-supplied FFmpeg binary remains a fallback for
/// codecs OpenCV's bundled backend cannot decode and for platforms without the Windows native runtime; when
/// neither can produce a frame, and when the source is larger than what may be read, the capability returns
/// no artifact: the metadata result stands on its own and the browser shows the honest "no preview stored"
/// state. Nothing is ever downloaded.
/// </remarks>
public sealed class VideoPosterPlugin : IMediaPreviewCapability
{
    private const int PipeChunkBytes = 256 * 1024;

    private readonly VideoPluginOptions options;
    private readonly IVideoFrameExtractor extractor;

    public VideoPosterPlugin()
        : this(VideoPluginOptions.Default)
    {
    }

    public VideoPosterPlugin(VideoPluginOptions options)
        : this(options, new CompositeVideoFrameExtractor(options))
    {
    }

    /// <summary>Hosts and tests may supply their own extractor; discovery then never runs.</summary>
    public VideoPosterPlugin(VideoPluginOptions options, IVideoFrameExtractor extractor)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
    }

    public MediaDescriptor Descriptor =>
        new(VideoSchema.Extensions, VideoSchema.ContentTypes, VideoSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (!extractor.IsAvailable)
        {
            return new MediaPreviewResult([]);
        }

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0 || length > options.MaxPosterSourceBytes)
        {
            // Too large to hand to a decoder within the host budget; metadata-only is the documented outcome.
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        // Probe first so a source that is not a readable video container never reaches the decoder.
        _ = await VideoProber.ProbeAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.3));

        var frame = await extractor.ExtractJpegAsync(
            (stream, token) => PipeSourceAsync(source, stream, length, token),
            options.PosterTimestampSeconds,
            options.MaxPosterDimension,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new MediaProgress(1));

        if (frame is null || frame.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact("image/jpeg", frame)]);
    }

    private static async Task PipeSourceAsync(IMediaSource source, Stream target, long length, CancellationToken cancellationToken)
    {
        var offset = 0L;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = (int)Math.Min(PipeChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.IsEmpty)
            {
                break;
            }

            await target.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            offset += chunk.Length;
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
