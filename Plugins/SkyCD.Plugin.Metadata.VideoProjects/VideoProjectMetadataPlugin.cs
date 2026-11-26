using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;
using SkyCD.Plugin.Metadata.VideoProjects.Models;
using SkyCD.Plugin.Metadata.VideoProjects.Parsing;

namespace SkyCD.Plugin.Metadata.VideoProjects;

/// <summary>
/// Publishes editing facts from video project files: the application and its version, the project title, the
/// timelines, track, clip and item counts, frame rate, duration and resolution, plus how many media files the
/// project references. Referenced media are only counted; they are never opened.
/// </summary>
/// <remarks>
/// Each format is recognized by its container bytes rather than by the extension, so a renamed or unrelated
/// file yields no values. A damaged or unsupported project also yields no values instead of a failure, so a
/// scan never stops on one file. A bound the project itself exceeds is not swallowed: the host records that
/// this provider refused to read the file.
/// </remarks>
public sealed class VideoProjectMetadataPlugin : IMediaMetadataCapability
{
    private const int ProviderPriority = 10;

    private readonly VideoProjectPluginOptions options;

    public VideoProjectMetadataPlugin()
        : this(VideoProjectPluginOptions.Default)
    {
    }

    public VideoProjectMetadataPlugin(VideoProjectPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(VideoProjectSchema.Extensions, VideoProjectSchema.ContentTypes, VideoProjectSchema.Metadata,
            Priority: ProviderPriority);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        VideoProjectFacts? facts;
        try
        {
            progress?.Report(new MediaProgress(0.2));
            facts = await VideoProjectReader
                .ReadAsync(item, source, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VideoProjectDecodeException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        progress?.Report(new MediaProgress(0.8));
        IReadOnlyList<MediaMetadataValue> values = facts is null ? [] : BuildValues(facts);
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    /// <summary>Projects the recovered facts onto the published schema, omitting everything absent.</summary>
    internal static List<MediaMetadataValue> BuildValues(VideoProjectFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var values = new List<MediaMetadataValue>
        {
            new(VideoProjectSchema.Kind, MediaMetadataType.Text, Text: facts.Kind),
            new(VideoProjectSchema.Format, MediaMetadataType.Text, Text: facts.Format),
            new(VideoProjectSchema.Application, MediaMetadataType.Text, Text: facts.Application)
        };

        AddText(values, VideoProjectSchema.ApplicationVersion, facts.ApplicationVersion);
        AddText(values, VideoProjectSchema.ProjectTitle, facts.ProjectTitle);
        AddInteger(values, VideoProjectSchema.TimelineCount, facts.TimelineCount);
        AddText(values, VideoProjectSchema.TimelineNames,
            facts.TimelineNames.Count > 0 ? string.Join("; ", facts.TimelineNames) : null);
        AddInteger(values, VideoProjectSchema.TrackCount, facts.TrackCount);
        AddInteger(values, VideoProjectSchema.ClipCount, facts.ClipCount);
        AddInteger(values, VideoProjectSchema.ItemCount, facts.ItemCount);
        AddInteger(values, VideoProjectSchema.MediaFileCount, facts.MediaFileCount);

        if (facts.FrameRate is > 0)
        {
            values.Add(new MediaMetadataValue(
                VideoProjectSchema.FrameRate, MediaMetadataType.Number, Number: facts.FrameRate));
        }

        if (facts.DurationSeconds is > 0)
        {
            values.Add(new MediaMetadataValue(
                VideoProjectSchema.Duration, MediaMetadataType.Number, Number: facts.DurationSeconds));
        }

        AddText(values, VideoProjectSchema.Resolution, facts.Resolution);

        if (facts.IsTemplate is true)
        {
            values.Add(new MediaMetadataValue(
                VideoProjectSchema.IsTemplate, MediaMetadataType.Boolean, Boolean: true));
        }

        if (facts.Truncated)
        {
            values.Add(new MediaMetadataValue(
                VideoProjectSchema.Truncated, MediaMetadataType.Boolean, Boolean: true));
        }

        return values;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
        }
    }

    private static void AddInteger(List<MediaMetadataValue> values, string key, long? value)
    {
        if (value is >= 0)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: value));
        }
    }
}
