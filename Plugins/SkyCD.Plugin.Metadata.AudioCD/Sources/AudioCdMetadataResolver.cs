using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.AudioCD.Sources;
using SkyCD.Plugin.Metadata.AudioCD.Export;
using SkyCD.Plugin.Metadata.AudioCD.Models;
using SkyCD.Plugin.Metadata.AudioCD.Parsing;

namespace SkyCD.Plugin.Metadata.AudioCD.Sources;

/// <summary>
/// Turns a table of contents into a disc capture: queries the enabled sources in order, applies the disc and
/// track tag/cover options, and returns null when the disc cannot be identified. Network failures of any kind
/// are absorbed so that an unreachable or slow source never fails a scan.
/// </summary>
public sealed class AudioCdMetadataResolver
{
    private readonly IMusicBrainzLookup musicBrainz;
    private readonly IGnuDbLookup gnuDb;
    private readonly ICoverArtLookup coverArt;
    private readonly AudioCdCoverRenderer coverRenderer;

    public AudioCdMetadataResolver(IMusicBrainzLookup musicBrainz, IGnuDbLookup gnuDb, ICoverArtLookup coverArt,
        AudioCdCoverRenderer coverRenderer)
    {
        this.musicBrainz = musicBrainz;
        this.gnuDb = gnuDb;
        this.coverArt = coverArt;
        this.coverRenderer = coverRenderer;
    }

    public async Task<AudioCdCapture?> ResolveAsync(DiscToc toc, AudioCdSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.Offline || settings.Sources.Count == 0)
        {
            return null;
        }

        var musicBrainzDiscId = DiscIdCalculator.ComputeMusicBrainzDiscId(toc);
        var cddbDiscId = DiscIdCalculator.ComputeCddbDiscId(toc);

        var disc = await LookupAsync(musicBrainzDiscId, toc, cddbDiscId, settings, cancellationToken)
            .ConfigureAwait(false);
        if (disc is null)
        {
            return null;
        }

        var discValues = BuildDiscValues(disc, toc, musicBrainzDiscId, cddbDiscId, settings);
        var (discCover, trackCover) = await ResolveCoversAsync(disc.MusicBrainzReleaseId, settings, cancellationToken)
            .ConfigureAwait(false);
        var tracks = BuildTracks(disc, toc, settings, trackCover);

        return new AudioCdCapture(AudioCdSchema.Disc, discValues, AudioCdSchema.Track, tracks, discCover);
    }

    private async Task<DiscMetadata?> LookupAsync(string musicBrainzDiscId, DiscToc toc, string cddbDiscId,
        AudioCdSettings settings, CancellationToken cancellationToken)
    {
        foreach (var source in settings.Sources)
        {
            try
            {
                DiscMetadata? match = null;
                if (source == AudioCdSource.MusicBrainz)
                {
                    match = await musicBrainz.LookupAsync(musicBrainzDiscId, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (source == AudioCdSource.GnuDb)
                {
                    match = await gnuDb.LookupAsync(toc, cddbDiscId, cancellationToken).ConfigureAwait(false);
                }

                if (match is not null)
                {
                    return match;
                }
            }
            catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
            {
                // An unreachable, slow, or malformed source is skipped; the next source may still match.
            }
        }

        return null;
    }

    private async Task<(MediaPreviewArtifact? Disc, MediaPreviewArtifact? Track)> ResolveCoversAsync(
        string? releaseId, AudioCdSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || (!settings.DiscCovers && !settings.TrackCovers))
        {
            return (null, null);
        }

        var largest = Math.Max(settings.DiscCovers ? settings.DiscCoverSize : 0,
            settings.TrackCovers ? settings.TrackCoverSize : 0);

        CoverArtImage? image;
        try
        {
            image = await coverArt.FetchAsync(releaseId, largest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            return (null, null);
        }

        if (image is null)
        {
            return (null, null);
        }

        var discCover = settings.DiscCovers
            ? coverRenderer.Render(image.Data, image.ContentType, settings.DiscCoverSize)
            : null;
        var trackCover = settings.TrackCovers
            ? coverRenderer.Render(image.Data, image.ContentType, settings.TrackCoverSize)
            : null;
        return (discCover, trackCover);
    }

    private static List<MediaMetadataValue> BuildDiscValues(DiscMetadata disc, DiscToc toc, string musicBrainzDiscId,
        string cddbDiscId, AudioCdSettings settings)
    {
        var values = new List<MediaMetadataValue>();
        AddText(values, AudioCdSchema.Album, disc.Album);

        if (settings.DiscArtistTags)
        {
            AddText(values, AudioCdSchema.Artist, disc.Artist);
            AddText(values, AudioCdSchema.AlbumArtist, disc.AlbumArtist);
        }

        if (disc.Year is { } year)
        {
            values.Add(new MediaMetadataValue(AudioCdSchema.Year, MediaMetadataType.Integer, Integer: year));
        }

        AddText(values, AudioCdSchema.Genre, disc.Genre);
        AddText(values, AudioCdSchema.Country, disc.Country);
        AddText(values, AudioCdSchema.Label, disc.Label);
        values.Add(new MediaMetadataValue(AudioCdSchema.TrackCount, MediaMetadataType.Integer,
            Integer: toc.TrackCount));
        AddText(values, AudioCdSchema.MusicBrainzDiscId, musicBrainzDiscId);
        AddText(values, AudioCdSchema.CddbDiscId, cddbDiscId);
        AddText(values, AudioCdSchema.Source, disc.SourceKey);
        return values;
    }

    private static IReadOnlyList<AudioCdTrackCapture> BuildTracks(DiscMetadata disc, DiscToc toc,
        AudioCdSettings settings, MediaPreviewArtifact? cover)
    {
        var tracks = new List<AudioCdTrackCapture>(toc.TrackCount);

        for (var index = 0; index < toc.TrackCount; index++)
        {
            var number = toc.FirstTrack + index;
            var source = index < disc.Tracks.Count ? disc.Tracks[index] : null;

            var values = new List<MediaMetadataValue>();
            AddText(values, AudioCdSchema.Title, source?.Title);
            if (settings.TrackArtistTags)
            {
                AddText(values, AudioCdSchema.Artist, source?.Artist ?? disc.AlbumArtist);
            }

            AddText(values, AudioCdSchema.Album, disc.Album);
            values.Add(new MediaMetadataValue(AudioCdSchema.TrackNumber, MediaMetadataType.Integer,
                Integer: number));

            var endFrame = index + 1 < toc.TrackOffsetsFrames.Count
                ? toc.TrackOffsetsFrames[index + 1]
                : toc.LeadOutFrames;
            var lengthSeconds = Math.Round((endFrame - toc.TrackOffsetsFrames[index]) / 75d, 3);
            values.Add(new MediaMetadataValue(AudioCdSchema.Length, MediaMetadataType.Number,
                Number: lengthSeconds));

            tracks.Add(new AudioCdTrackCapture(number, values, settings.TrackCovers ? cover : null));
        }

        return tracks;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
        }
    }

    private static bool IsRecoverable(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
}
