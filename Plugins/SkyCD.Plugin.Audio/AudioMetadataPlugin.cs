using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Audio;

/// <summary>
/// Extracts normalized audio tags and technical stream facts. No playback or decoding of audio samples is
/// involved — only container and tag structures are read — and the source is never modified.
/// </summary>
/// <remarks>
/// A field the source does not carry is omitted rather than published as an empty string or zero, which is
/// what keeps "unknown" distinguishable from "none" in the catalog and in search.
/// </remarks>
public sealed class AudioMetadataPlugin : IMediaMetadataCapability
{
    private readonly AudioPluginOptions options;

    public AudioMetadataPlugin()
        : this(AudioPluginOptions.Default)
    {
    }

    public AudioMetadataPlugin(AudioPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(AudioSchema.Extensions, AudioSchema.ContentTypes, AudioSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var probe = await AudioProber.ProbeAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.8));

        var values = new List<MediaMetadataValue>
        {
            new(AudioSchema.Container, MediaMetadataType.Text, Text: probe.Container),
            new(AudioSchema.HasCoverArt, MediaMetadataType.Boolean, Boolean: probe.CoverArt is not null),
            new(AudioSchema.TagSource, MediaMetadataType.Text, Text: probe.Tags.Source ?? "none")
        };

        AddText(values, AudioSchema.Codec, probe.Codec);
        AddText(values, AudioSchema.Title, probe.Tags.Title);
        AddText(values, AudioSchema.Artist, probe.Tags.Artist);
        AddText(values, AudioSchema.Album, probe.Tags.Album);
        AddText(values, AudioSchema.AlbumArtist, probe.Tags.AlbumArtist);
        AddText(values, AudioSchema.Genre, probe.Tags.Genre);
        AddInteger(values, AudioSchema.TrackNumber, probe.Tags.TrackNumber);
        AddInteger(values, AudioSchema.TrackCount, probe.Tags.TrackCount);
        AddInteger(values, AudioSchema.DiscNumber, probe.Tags.DiscNumber);
        AddInteger(values, AudioSchema.DiscCount, probe.Tags.DiscCount);
        AddInteger(values, AudioSchema.Year, probe.Tags.Year);
        AddInteger(values, AudioSchema.Bitrate, probe.BitrateKbps);
        AddInteger(values, AudioSchema.SampleRate, probe.SampleRateHz);
        AddInteger(values, AudioSchema.Channels, probe.Channels);
        AddInteger(values, AudioSchema.BitDepth, probe.BitDepth);

        if (probe.DurationSeconds is > 0)
        {
            // Rounded to milliseconds: enough to be exact for display and sorting, stable as a cache value.
            values.Add(new MediaMetadataValue(
                AudioSchema.Duration,
                MediaMetadataType.Number,
                Number: Math.Round(probe.DurationSeconds.Value, 3)));
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
