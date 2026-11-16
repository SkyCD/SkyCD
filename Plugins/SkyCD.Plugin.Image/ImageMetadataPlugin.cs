using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Extracts typed image metadata from the container header and EXIF, without rendering pixels. Dimensions
/// are reported as displayed, so a rotated photo reports the size the user sees rather than the stored one;
/// the stored orientation and the rotation needed to reach it are published alongside.
/// </summary>
public sealed class ImageMetadataPlugin : IMediaMetadataCapability
{
    private readonly ImagePluginOptions options;

    public ImageMetadataPlugin()
        : this(ImagePluginOptions.Default)
    {
    }

    public ImageMetadataPlugin(ImagePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(ImageSchema.Extensions, ImageSchema.ContentTypes, ImageSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await ImageSourceReader.ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        var probe = ImageDecoder.Probe(data);
        progress?.Report(new MediaProgress(0.7));

        // Best-effort: geometry is already known, so a source without EXIF still yields a full result.
        var capturedAt = ExifReader.TryReadCapturedAt(data);
        progress?.Report(new MediaProgress(1));

        var values = new List<MediaMetadataValue>
        {
            new(ImageSchema.Width, MediaMetadataType.Integer, Integer: probe.Width),
            new(ImageSchema.Height, MediaMetadataType.Integer, Integer: probe.Height),
            new(ImageSchema.Format, MediaMetadataType.Text, Text: probe.Format),
            new(ImageSchema.HasAlpha, MediaMetadataType.Boolean, Boolean: probe.HasAlpha),
            new(ImageSchema.Orientation, MediaMetadataType.Integer, Integer: probe.Orientation),
            new(ImageSchema.RotationDegrees, MediaMetadataType.Integer, Integer: probe.RotationDegrees),
            new(ImageSchema.FrameCount, MediaMetadataType.Integer, Integer: probe.FrameCount),
            new(ImageSchema.IsAnimated, MediaMetadataType.Boolean, Boolean: probe.IsAnimated)
        };

        if (capturedAt is { } timestamp)
        {
            values.Add(new MediaMetadataValue(
                ImageSchema.CapturedAt, MediaMetadataType.Timestamp, Timestamp: timestamp));
        }

        return new MediaExtractionResult(values);
    }
}
