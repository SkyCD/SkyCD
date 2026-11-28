using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Sources;

namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Models;

/// <summary>
/// Normalized, persisted audio CD settings. Every value is clamped to a supported range so a hand-edited or
/// stale setting can never disable a scan or request an unbounded image. Disc and track records are configured
/// independently, including their own cover size.
/// </summary>
public sealed class AudioCdSettings
{
    public const string SourcesKey = "sources";
    public const string DiscArtistTagsKey = "discArtistTags";
    public const string TrackArtistTagsKey = "trackArtistTags";
    public const string DiscCoversKey = "discCovers";
    public const string TrackCoversKey = "trackCovers";
    public const string DiscCoverSizeKey = "discCoverSize";
    public const string TrackCoverSizeKey = "trackCoverSize";
    public const string OfflineKey = "offline";
    public const string RequestTimeoutSecondsKey = "requestTimeoutSeconds";

    public const int MinCoverSize = 32;
    public const int MaxCoverSize = 2048;
    public const int DefaultCoverSize = 250;

    private AudioCdSettings(IReadOnlyList<AudioCdSource> sources, bool discArtistTags, bool trackArtistTags,
        bool discCovers, bool trackCovers, int discCoverSize, int trackCoverSize, bool offline,
        int requestTimeoutSeconds)
    {
        Sources = sources;
        DiscArtistTags = discArtistTags;
        TrackArtistTags = trackArtistTags;
        DiscCovers = discCovers;
        TrackCovers = trackCovers;
        DiscCoverSize = discCoverSize;
        TrackCoverSize = trackCoverSize;
        Offline = offline;
        RequestTimeoutSeconds = requestTimeoutSeconds;
    }

    /// <summary>Enabled online sources in query order; empty when the user disabled every source.</summary>
    public IReadOnlyList<AudioCdSource> Sources { get; }

    /// <summary>Whether the disc record carries the artist name as a tag.</summary>
    public bool DiscArtistTags { get; }

    /// <summary>Whether each track record carries the artist name as a tag.</summary>
    public bool TrackArtistTags { get; }

    /// <summary>Whether the disc record downloads and stores a cover image.</summary>
    public bool DiscCovers { get; }

    /// <summary>Whether each track record downloads and stores a cover image.</summary>
    public bool TrackCovers { get; }

    /// <summary>Longest-edge pixel size for the disc cover.</summary>
    public int DiscCoverSize { get; }

    /// <summary>Longest-edge pixel size for each track cover.</summary>
    public int TrackCoverSize { get; }

    /// <summary>When true, no network request is made and the disc is treated as unidentified.</summary>
    public bool Offline { get; }

    /// <summary>Per-request network timeout.</summary>
    public int RequestTimeoutSeconds { get; }

    public static AudioCdSettings Default { get; } = new(
        [AudioCdSource.MusicBrainz, AudioCdSource.GnuDb],
        discArtistTags: true,
        trackArtistTags: true,
        discCovers: true,
        trackCovers: true,
        discCoverSize: DefaultCoverSize,
        trackCoverSize: DefaultCoverSize,
        offline: false,
        requestTimeoutSeconds: 8);

    /// <summary>Normalizes persisted settings; missing or malformed values fall back to their defaults.</summary>
    public static AudioCdSettings Parse(IReadOnlyDictionary<string, string?>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return Default;
        }

        return new AudioCdSettings(
            ParseSources(Read(settings, SourcesKey)),
            ReadBool(settings, DiscArtistTagsKey, Default.DiscArtistTags),
            ReadBool(settings, TrackArtistTagsKey, Default.TrackArtistTags),
            ReadBool(settings, DiscCoversKey, Default.DiscCovers),
            ReadBool(settings, TrackCoversKey, Default.TrackCovers),
            ReadInt(settings, DiscCoverSizeKey, Default.DiscCoverSize, MinCoverSize, MaxCoverSize),
            ReadInt(settings, TrackCoverSizeKey, Default.TrackCoverSize, MinCoverSize, MaxCoverSize),
            ReadBool(settings, OfflineKey, Default.Offline),
            ReadInt(settings, RequestTimeoutSecondsKey, Default.RequestTimeoutSeconds, 1, 30));
    }

    /// <summary>Produces the persisted key/value form, used by the configuration surface.</summary>
    public IReadOnlyDictionary<string, string?> ToSettings()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [SourcesKey] = string.Join(';', Sources.Select(ToKey)),
            [DiscArtistTagsKey] = DiscArtistTags.ToString(),
            [TrackArtistTagsKey] = TrackArtistTags.ToString(),
            [DiscCoversKey] = DiscCovers.ToString(),
            [TrackCoversKey] = TrackCovers.ToString(),
            [DiscCoverSizeKey] = DiscCoverSize.ToString(CultureInfo.InvariantCulture),
            [TrackCoverSizeKey] = TrackCoverSize.ToString(CultureInfo.InvariantCulture),
            [OfflineKey] = Offline.ToString(),
            [RequestTimeoutSecondsKey] = RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyList<AudioCdSource> ParseSources(string? value)
    {
        // A missing key means "never configured" and keeps the defaults; an empty value means the user
        // disabled every source and must stay disabled.
        if (value is null)
        {
            return Default.Sources;
        }

        var sources = new List<AudioCdSource>();
        foreach (var token in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseSource(token, out var source) && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    /// <summary>Parses one source token; recognized regardless of case.</summary>
    public static bool TryParseSource(string token, out AudioCdSource source)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "musicbrainz":
                source = AudioCdSource.MusicBrainz;
                return true;
            case "gnudb":
                source = AudioCdSource.GnuDb;
                return true;
            default:
                source = default;
                return false;
        }
    }

    /// <summary>The persisted token for a source.</summary>
    public static string ToKey(AudioCdSource source) => source switch
    {
        AudioCdSource.MusicBrainz => "musicbrainz",
        AudioCdSource.GnuDb => "gnudb",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown audio CD source.")
    };

    private static string? Read(IReadOnlyDictionary<string, string?> settings, string key) =>
        settings.TryGetValue(key, out var value) ? value : null;

    private static bool ReadBool(IReadOnlyDictionary<string, string?> settings, string key, bool fallback) =>
        bool.TryParse(Read(settings, key), out var value) ? value : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string?> settings, string key, int fallback, int min, int max)
    {
        if (!int.TryParse(Read(settings, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
