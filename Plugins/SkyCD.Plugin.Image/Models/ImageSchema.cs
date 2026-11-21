using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Image.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the image plugin. Field keys and
/// units are part of the cached-data contract: changing the meaning or unit of a field requires a new
/// schema version, because the host keys stored results by schema id and version.
/// </summary>
public static class ImageSchema
{
    public const string MetadataSchemaId = "skycd.image.metadata";

    /// <summary>Version 2 adds the embedded <c>Tags</c>, <c>Comment</c> and <c>Rating</c> fields.</summary>
    public const int MetadataSchemaVersion = 2;

    public const string PreviewSchemaId = "skycd.image.preview";
    public const int PreviewSchemaVersion = 1;

    /// <summary>Tags read from TIFF containers the thumbnail decoder does not present.</summary>
    public const string ContainerTagsSchemaId = "skycd.image.tags";
    public const int ContainerTagsSchemaVersion = 1;

    /// <summary>Oriented width in pixels, after applying EXIF rotation.</summary>
    public const string Width = "Width";

    /// <summary>Oriented height in pixels, after applying EXIF rotation.</summary>
    public const string Height = "Height";

    /// <summary>Container format in lowercase, for example <c>jpeg</c>, <c>png</c>, <c>webp</c> or <c>gif</c>.</summary>
    public const string Format = "Format";

    public const string HasAlpha = "HasAlpha";

    /// <summary>EXIF orientation as stored, 1 to 8; 1 when the source carries no orientation.</summary>
    public const string Orientation = "Orientation";

    /// <summary>Clockwise rotation the host must apply to display the image upright.</summary>
    public const string RotationDegrees = "RotationDegrees";

    /// <summary>Frames in the container; 1 for a still image.</summary>
    public const string FrameCount = "FrameCount";

    /// <summary>True when the container holds more than one frame. Only the first frame is previewed.</summary>
    public const string IsAnimated = "IsAnimated";

    /// <summary>Capture date from EXIF when present; omitted entirely when the source has none.</summary>
    public const string CapturedAt = "CapturedAt";

    /// <summary>Embedded keywords joined with <c>; </c>; omitted when the source carries none.</summary>
    public const string Tags = "Tags";

    /// <summary>Embedded caption or comment; omitted entirely when the source carries none.</summary>
    public const string Comment = "Comment";

    /// <summary>Embedded star rating from 0 to 5; omitted when the source carries none.</summary>
    public const string Rating = "Rating";

    /// <summary>Extensions the plugin claims, including the leading dot.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    /// <summary>
    /// TIFF containers, including the TIFF-based RAW formats. Their geometry is not decoded, but their tag
    /// carriers are in every photo tool's path, so they are claimed by a tags-only capability.
    /// </summary>
    public static readonly IReadOnlyList<string> ContainerExtensions =
        [".tif", ".tiff", ".dng", ".cr2", ".nef", ".arw"];

    /// <summary>MIME types the plugin claims.</summary>
    public static readonly IReadOnlyList<string> ContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public static readonly IReadOnlyList<string> ContainerContentTypes = ["image/tiff"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Width, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(Height, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(Format, MediaMetadataType.Text),
        new MediaMetadataField(HasAlpha, MediaMetadataType.Boolean),
        new MediaMetadataField(Orientation, MediaMetadataType.Integer),
        new MediaMetadataField(RotationDegrees, MediaMetadataType.Integer, "deg"),
        new MediaMetadataField(FrameCount, MediaMetadataType.Integer),
        new MediaMetadataField(IsAnimated, MediaMetadataType.Boolean),
        new MediaMetadataField(CapturedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(Tags, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
        new MediaMetadataField(Comment, MediaMetadataType.Text),
        new MediaMetadataField(Rating, MediaMetadataType.Integer)
    ]);

    /// <summary>
    /// Preview results carry artifacts rather than values, so the schema is identity only.
    /// </summary>
    public static MediaMetadataSchema Preview { get; } =
        new(PreviewSchemaId, PreviewSchemaVersion, []);

    /// <summary>Same fields as the metadata schema, published for containers whose pixels are not decoded.</summary>
    public static MediaMetadataSchema ContainerTags { get; } =
        new(ContainerTagsSchemaId, ContainerTagsSchemaVersion,
        [
            new MediaMetadataField(Tags, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
            new MediaMetadataField(Comment, MediaMetadataType.Text),
            new MediaMetadataField(Rating, MediaMetadataType.Integer)
        ]);
}
