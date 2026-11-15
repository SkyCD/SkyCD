using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Renders one bounded thumbnail per image. Animated containers are represented by their first frame only;
/// the frame count stays available through <see cref="ImageMetadataPlugin"/> so a host can label the preview
/// as a still from an animation. The original is only ever read, never modified.
/// </summary>
public sealed class ImagePreviewPlugin : IMediaPreviewCapability
{
    private readonly ImagePluginOptions options;

    public ImagePreviewPlugin()
        : this(ImagePluginOptions.Default)
    {
    }

    public ImagePreviewPlugin(ImagePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(ImageSchema.Extensions, ImageSchema.ContentTypes, ImageSchema.Preview);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ImageSourceGuard.Validate(item, source, options.MaxSourceBytes);

        using var stream = new MediaSourceStream(source, cancellationToken);
        var probe = ImageDecoder.Probe(stream);
        progress?.Report(new MediaProgress(0.2));

        var thumbnail = ImageDecoder.RenderThumbnail(
            stream, probe, options.MaxThumbnailDimension, options.JpegQuality, cancellationToken);
        progress?.Report(new MediaProgress(1));

        return Task.FromResult(new MediaPreviewResult(
            [new MediaPreviewArtifact(thumbnail.ContentType, thumbnail.Data)]));
    }
}
