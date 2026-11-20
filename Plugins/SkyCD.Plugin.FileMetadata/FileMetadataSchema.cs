using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Stable schema identities, field keys and claimed source types published by the file metadata plugin.
/// Field keys and their meaning are part of the cached-data contract, because the host keys stored results
/// by schema id and version.
/// </summary>
public static class FileMetadataSchema
{
    /// <summary>Tags, comment and rating stored inside TIFF containers and XMP sidecars.</summary>
    public const string EmbeddedSchemaId = "skycd.file.metadata";
    public const int EmbeddedSchemaVersion = 1;

    /// <summary>Tags, comment and rating a user assigned through the operating system.</summary>
    public const string OsSchemaId = "skycd.file.os-metadata";
    public const int OsSchemaVersion = 1;

    public const string Tags = "Tags";
    public const string Comment = "Comment";
    public const string Rating = "Rating";

    /// <summary>
    /// TIFF containers, including the TIFF-based RAW formats, and standalone XMP sidecars. The image
    /// formats the image plugin presents are read there instead, so every carrier is claimed once.
    /// </summary>
    public static readonly IReadOnlyList<string> EmbeddedExtensions =
        [".tif", ".tiff", ".dng", ".cr2", ".nef", ".arw", ".xmp"];

    public static readonly IReadOnlyList<string> EmbeddedContentTypes = ["image/tiff", "application/rdf+xml"];

    /// <summary>OS-assigned metadata can exist next to any file a user catalogues.</summary>
    public static readonly IReadOnlyList<string> AnyExtensions = ["*"];

    public static MediaMetadataSchema Embedded { get; } =
        new(EmbeddedSchemaId, EmbeddedSchemaVersion, Fields());

    public static MediaMetadataSchema Os { get; } =
        new(OsSchemaId, OsSchemaVersion, Fields());

    private static MediaMetadataField[] Fields() =>
    [
        new(Tags, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
        new(Comment, MediaMetadataType.Text),
        new(Rating, MediaMetadataType.Integer)
    ];
}
