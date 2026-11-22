using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Video.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the video plugin. Field keys and units
/// are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class VideoSchema
{
    public const string MetadataSchemaId = "skycd.video.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.video.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string Duration = "Duration";
    public const string Width = "Width";
    public const string Height = "Height";
    public const string RotationDegrees = "RotationDegrees";
    public const string VideoCodec = "VideoCodec";
    public const string AudioCodec = "AudioCodec";
    public const string FrameRate = "FrameRate";
    public const string Bitrate = "Bitrate";
    public const string VideoStreamCount = "VideoStreamCount";
    public const string AudioStreamCount = "AudioStreamCount";
    public const string Title = "Title";

    /// <summary>
    /// Extensions the plugin claims. <c>.m4a</c> is absent because that is the audio plugin's M4A entry
    /// point; <c>.mp4</c> and <c>.m4v</c> are video containers and belong here.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions =
        [".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi"];

    public static readonly IReadOnlyList<string> ContentTypes =
        ["video/mp4", "video/quicktime", "video/x-matroska", "video/webm", "video/x-msvideo", "video/avi"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Duration, MediaMetadataType.Number, "s"),
        new MediaMetadataField(Width, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(Height, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(RotationDegrees, MediaMetadataType.Integer, "deg"),
        new MediaMetadataField(VideoCodec, MediaMetadataType.Text),
        new MediaMetadataField(AudioCodec, MediaMetadataType.Text),
        new MediaMetadataField(FrameRate, MediaMetadataType.Number, "fps"),
        new MediaMetadataField(Bitrate, MediaMetadataType.Integer, "kbps"),
        new MediaMetadataField(VideoStreamCount, MediaMetadataType.Integer),
        new MediaMetadataField(AudioStreamCount, MediaMetadataType.Integer),
        new MediaMetadataField(Title, MediaMetadataType.Text)
    ]);

    /// <summary>Preview results carry the poster frame itself rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
