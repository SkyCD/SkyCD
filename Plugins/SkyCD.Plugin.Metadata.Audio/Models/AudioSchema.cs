using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Audio.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the audio plugin. Field keys and units
/// are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class AudioSchema
{
    public const string MetadataSchemaId = "skycd.audio.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.audio.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Title = "Title";
    public const string Artist = "Artist";
    public const string Album = "Album";
    public const string AlbumArtist = "AlbumArtist";
    public const string Genre = "Genre";
    public const string TrackNumber = "TrackNumber";
    public const string TrackCount = "TrackCount";
    public const string DiscNumber = "DiscNumber";
    public const string DiscCount = "DiscCount";
    public const string Year = "Year";

    /// <summary>Playback length in seconds.</summary>
    public const string Duration = "Duration";

    public const string Bitrate = "Bitrate";
    public const string SampleRate = "SampleRate";
    public const string Channels = "Channels";
    public const string BitDepth = "BitDepth";

    /// <summary>Codec of the audio stream, for example <c>mp3</c>, <c>flac</c>, <c>vorbis</c> or <c>aac</c>.</summary>
    public const string Codec = "Codec";

    /// <summary>Container detected from the bytes rather than the file name, for example <c>mp3</c> or <c>mp4</c>.</summary>
    public const string Container = "Container";

    public const string HasCoverArt = "HasCoverArt";

    /// <summary>Where the tags came from, for example <c>id3v2.3</c>, <c>id3v1</c>, <c>vorbis-comment</c> or <c>itunes-atom</c>.</summary>
    public const string TagSource = "TagSource";

    /// <summary>
    /// Extensions the plugin claims. <c>.mp4</c> is deliberately absent: that container is claimed by the
    /// video plugin, so an M4A has to be catalogued with an audio extension or MIME type.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions =
        [".mp3", ".flac", ".ogg", ".oga", ".m4a", ".m4b"];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "audio/mpeg", "audio/mp3", "audio/flac", "audio/x-flac",
        "audio/ogg", "application/ogg", "audio/mp4", "audio/x-m4a"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Artist, MediaMetadataType.Text),
        new MediaMetadataField(Album, MediaMetadataType.Text),
        new MediaMetadataField(AlbumArtist, MediaMetadataType.Text),
        new MediaMetadataField(Genre, MediaMetadataType.Text),
        new MediaMetadataField(TrackNumber, MediaMetadataType.Integer),
        new MediaMetadataField(TrackCount, MediaMetadataType.Integer),
        new MediaMetadataField(DiscNumber, MediaMetadataType.Integer),
        new MediaMetadataField(DiscCount, MediaMetadataType.Integer),
        new MediaMetadataField(Year, MediaMetadataType.Integer),
        new MediaMetadataField(Duration, MediaMetadataType.Number, "s"),
        new MediaMetadataField(Bitrate, MediaMetadataType.Integer, "kbps"),
        new MediaMetadataField(SampleRate, MediaMetadataType.Integer, "Hz"),
        new MediaMetadataField(Channels, MediaMetadataType.Integer),
        new MediaMetadataField(BitDepth, MediaMetadataType.Integer, "bit"),
        new MediaMetadataField(Codec, MediaMetadataType.Text),
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(HasCoverArt, MediaMetadataType.Boolean),
        new MediaMetadataField(TagSource, MediaMetadataType.Text)
    ]);

    /// <summary>Preview results carry the embedded picture itself rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
