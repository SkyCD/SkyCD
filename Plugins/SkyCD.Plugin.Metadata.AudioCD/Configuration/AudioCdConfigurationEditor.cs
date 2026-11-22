using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.Metadata.AudioCD.Sources;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Configuration;

/// <summary>
/// Editable state of the audio CD configuration surface. It reads persisted settings, exposes them as typed
/// properties, and normalizes them back through <see cref="AudioCdSettings"/> when built, so the values that are
/// saved are exactly the values the capture code will use.
/// </summary>
public sealed class AudioCdConfigurationEditor
{
    public AudioCdConfigurationEditor(IReadOnlyDictionary<string, string?>? currentSettings = null)
    {
        var initial = AudioCdSettings.Parse(currentSettings);
        MusicBrainzEnabled = initial.Sources.Contains(AudioCdSource.MusicBrainz);
        GnuDbEnabled = initial.Sources.Contains(AudioCdSource.GnuDb);
        DiscArtistTags = initial.DiscArtistTags;
        TrackArtistTags = initial.TrackArtistTags;
        DiscCovers = initial.DiscCovers;
        TrackCovers = initial.TrackCovers;
        DiscCoverSize = initial.DiscCoverSize;
        TrackCoverSize = initial.TrackCoverSize;
        Offline = initial.Offline;
        RequestTimeoutSeconds = initial.RequestTimeoutSeconds;
    }

    public bool MusicBrainzEnabled { get; set; }

    public bool GnuDbEnabled { get; set; }

    public bool DiscArtistTags { get; set; }

    public bool TrackArtistTags { get; set; }

    public bool DiscCovers { get; set; }

    public bool TrackCovers { get; set; }

    public int DiscCoverSize { get; set; }

    public int TrackCoverSize { get; set; }

    public bool Offline { get; set; }

    public int RequestTimeoutSeconds { get; set; }

    public IReadOnlyDictionary<string, string?> BuildSettings()
    {
        var sources = new List<AudioCdSource>();
        if (MusicBrainzEnabled)
        {
            sources.Add(AudioCdSource.MusicBrainz);
        }

        if (GnuDbEnabled)
        {
            sources.Add(AudioCdSource.GnuDb);
        }

        // Round-trip through the parser so clamping and defaults match what the capture path will read.
        var normalized = new Dictionary<string, string?>
        {
            [AudioCdSettings.SourcesKey] = string.Join(';', sources.ConvertAll(AudioCdSettings.ToKey)),
            [AudioCdSettings.DiscArtistTagsKey] = DiscArtistTags.ToString(),
            [AudioCdSettings.TrackArtistTagsKey] = TrackArtistTags.ToString(),
            [AudioCdSettings.DiscCoversKey] = DiscCovers.ToString(),
            [AudioCdSettings.TrackCoversKey] = TrackCovers.ToString(),
            [AudioCdSettings.DiscCoverSizeKey] = DiscCoverSize.ToString(CultureInfo.InvariantCulture),
            [AudioCdSettings.TrackCoverSizeKey] = TrackCoverSize.ToString(CultureInfo.InvariantCulture),
            [AudioCdSettings.OfflineKey] = Offline.ToString(),
            [AudioCdSettings.RequestTimeoutSecondsKey] = RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
        };

        return AudioCdSettings.Parse(normalized).ToSettings();
    }
}
