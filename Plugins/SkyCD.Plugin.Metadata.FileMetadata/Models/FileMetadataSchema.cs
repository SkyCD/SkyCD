using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.FileMetadata.Models;

/// <summary>
/// Stable schema identity, field keys and claimed source types published by the file metadata plugin.
/// Field keys and their meaning are part of the cached-data contract, because the host keys stored results
/// by schema id and version.
/// </summary>
public static class FileMetadataSchema
{
    /// <summary>Tags, comment and rating a user assigned through the operating system.</summary>
    public const string OsSchemaId = "skycd.file.os-metadata";
    public const int OsSchemaVersion = 1;

    public const string Tags = "Tags";
    public const string Comment = "Comment";
    public const string Rating = "Rating";

    /// <summary>OS-assigned metadata can exist next to any file a user catalogues.</summary>
    public static readonly IReadOnlyList<string> AnyExtensions = ["*"];

    public static MediaMetadataSchema Os { get; } =
        new(OsSchemaId, OsSchemaVersion,
        [
            new MediaMetadataField(Tags, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
            new MediaMetadataField(Comment, MediaMetadataType.Text),
            new MediaMetadataField(Rating, MediaMetadataType.Integer)
        ]);
}
