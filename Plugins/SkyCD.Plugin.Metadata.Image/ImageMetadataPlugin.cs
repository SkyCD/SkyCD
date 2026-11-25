using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.MediaMetadata.Models;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;

namespace SkyCD.Plugin.Metadata.Image;

/// <summary>
/// Extracts typed image metadata from the container header, EXIF and the portable tag carriers, without
/// rendering pixels. Dimensions are reported as displayed, so a rotated photo reports the size the user sees
/// rather than the stored one; the stored orientation and the rotation needed to reach it are published
/// alongside, followed by the keywords, comment and rating the file itself carries.
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

        // Best-effort: geometry is already known, so a source without tag carriers still yields a full
        // result, and the reader maps a damaged carrier to no values instead of failing the item.
        var embedded = EmbeddedMetadataReader.Read(data);
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

        if (embedded.CapturedAt is { } timestamp)
        {
            values.Add(new MediaMetadataValue(
                ImageSchema.CapturedAt, MediaMetadataType.Timestamp, Timestamp: timestamp));
        }

        values.AddRange(MetadataValues.Build(embedded.Tags, ImageSchema.Tags, ImageSchema.Comment, ImageSchema.Rating));

        return new MediaExtractionResult(values);
    }
}
