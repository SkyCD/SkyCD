using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AudioCd;

/// <summary>
/// Stable schema identities, field keys and units published by the audio CD plugin. Keys and units are part of
/// the cached-data contract, so changing a field's meaning or unit requires a new schema version.
/// </summary>
public static class AudioCdSchema
{
    public const string DiscSchemaId = "skycd.audio-cd.disc";
    public const int DiscSchemaVersion = 1;

    public const string TrackSchemaId = "skycd.audio-cd.track";
    public const int TrackSchemaVersion = 1;

    public const string Album = "Album";
    public const string Artist = "Artist";
    public const string AlbumArtist = "AlbumArtist";
    public const string Year = "Year";
    public const string Genre = "Genre";
    public const string Country = "Country";
    public const string Label = "Label";
    public const string TrackCount = "TrackCount";

    /// <summary>The MusicBrainz disc id computed from the table of contents.</summary>
    public const string MusicBrainzDiscId = "MusicBrainzDiscId";

    /// <summary>The freedb/CDDB disc id computed from the table of contents.</summary>
    public const string CddbDiscId = "CddbDiscId";

    /// <summary>Which online source supplied the disc details, for example <c>musicbrainz</c> or <c>gnudb</c>.</summary>
    public const string Source = "Source";

    public const string Title = "Title";
    public const string TrackNumber = "TrackNumber";

    /// <summary>Track playing length in seconds.</summary>
    public const string Length = "Length";

    public static MediaMetadataSchema Disc { get; } = new(DiscSchemaId, DiscSchemaVersion,
    [
        new MediaMetadataField(Album, MediaMetadataType.Text),
        new MediaMetadataField(Artist, MediaMetadataType.Text),
        new MediaMetadataField(AlbumArtist, MediaMetadataType.Text),
        new MediaMetadataField(Year, MediaMetadataType.Integer),
        new MediaMetadataField(Genre, MediaMetadataType.Text),
        new MediaMetadataField(Country, MediaMetadataType.Text),
        new MediaMetadataField(Label, MediaMetadataType.Text),
        new MediaMetadataField(TrackCount, MediaMetadataType.Integer),
        new MediaMetadataField(MusicBrainzDiscId, MediaMetadataType.Text),
        new MediaMetadataField(CddbDiscId, MediaMetadataType.Text),
        new MediaMetadataField(Source, MediaMetadataType.Text)
    ]);

    public static MediaMetadataSchema Track { get; } = new(TrackSchemaId, TrackSchemaVersion,
    [
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Artist, MediaMetadataType.Text),
        new MediaMetadataField(Album, MediaMetadataType.Text),
        new MediaMetadataField(TrackNumber, MediaMetadataType.Integer),
        new MediaMetadataField(Length, MediaMetadataType.Number, "s")
    ]);

    /// <summary>The extension Windows uses for the virtual track entries of a mounted audio CD.</summary>
    public static readonly IReadOnlyList<string> TrackExtensions = [".cda"];
}
