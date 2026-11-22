using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Video.Models;
using SkyCD.Plugin.Metadata.Video.Parsing;

namespace SkyCD.Plugin.Metadata.Video;

/// <summary>
/// Extracts container metadata from MP4, QuickTime, Matroska, WebM and AVI sources. No video frames are
/// decoded and the original is never modified.
/// </summary>
/// <remarks>
/// A field the container does not carry is omitted rather than published as zero, so an AVI file with no
/// rotation metadata simply has no <see cref="VideoSchema.RotationDegrees"/> field instead of claiming to be
/// upright.
/// </remarks>
public sealed class VideoMetadataPlugin : IMediaMetadataCapability
{
    private readonly VideoPluginOptions options;

    public VideoMetadataPlugin()
        : this(VideoPluginOptions.Default)
    {
    }

    public VideoMetadataPlugin(VideoPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(VideoSchema.Extensions, VideoSchema.ContentTypes, VideoSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var probe = await VideoProber.ProbeAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.8));

        var values = new List<MediaMetadataValue>
        {
            new(VideoSchema.Container, MediaMetadataType.Text, Text: probe.Container)
        };

        AddText(values, VideoSchema.VideoCodec, probe.VideoCodec);
        AddText(values, VideoSchema.AudioCodec, probe.AudioCodec);
        AddText(values, VideoSchema.Title, probe.Title);
        AddInteger(values, VideoSchema.Width, probe.Width);
        AddInteger(values, VideoSchema.Height, probe.Height);
        AddInteger(values, VideoSchema.Bitrate, probe.BitrateKbps);

        // Rotation is meaningful at zero — it means the container declared the video upright — so unlike the
        // other counts it is published whenever present. Containers with no rotation concept omit it.
        if (probe.RotationDegrees is { } rotation)
        {
            values.Add(new MediaMetadataValue(
                VideoSchema.RotationDegrees, MediaMetadataType.Integer, Integer: rotation));
        }

        if (probe.DurationSeconds is > 0)
        {
            // Rounded to milliseconds: exact enough to display and sort, stable as a cached value.
            values.Add(new MediaMetadataValue(
                VideoSchema.Duration, MediaMetadataType.Number, Number: Math.Round(probe.DurationSeconds.Value, 3)));
        }

        if (probe.FrameRate is > 0)
        {
            values.Add(new MediaMetadataValue(
                VideoSchema.FrameRate, MediaMetadataType.Number, Number: probe.FrameRate.Value));
        }

        if (probe.VideoStreamCount > 0)
        {
            values.Add(new MediaMetadataValue(
                VideoSchema.VideoStreamCount, MediaMetadataType.Integer, Integer: probe.VideoStreamCount));
        }

        if (probe.AudioStreamCount > 0)
        {
            values.Add(new MediaMetadataValue(
                VideoSchema.AudioStreamCount, MediaMetadataType.Integer, Integer: probe.AudioStreamCount));
        }

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
        }
    }

    private static void AddInteger(List<MediaMetadataValue> values, string key, int? number)
    {
        if (number is > 0)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: number));
        }
    }
}
