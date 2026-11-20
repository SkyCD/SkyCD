using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Stable schema identity, field keys and claimed source types published by the file metadata plugin. Field
/// keys and their meaning are part of the cached-data contract, because the host keys stored results by
/// schema id and version.
/// </summary>
public static class FileMetadataSchema
{
    public const string SchemaId = "skycd.file.metadata";
    public const int SchemaVersion = 1;

    /// <summary>Flat keywords separated by semicolons, as written by Windows, Lightroom, digiKam and IPTC.</summary>
    public const string Keywords = "Keywords";

    /// <summary>
    /// Hierarchical keyword paths. Each published tag is one semicolon-free token; the plugin joins levels
    /// with <c>" / "</c> so a flat catalog tag list keeps the hierarchy readable.
    /// </summary>
    public const string HierarchicalKeywords = "HierarchicalKeywords";

    /// <summary>The user comment or caption; omitted entirely when the source carries none.</summary>
    public const string Comment = "Comment";

    /// <summary>Star rating from 0 to 5, normalized from the 0–99 scale some Windows tools persist.</summary>
    public const string Rating = "Rating";

    public const int MaxCommentLength = 2000;
    public const int MaxTagLength = 128;
    public const int MaxTagCount = 64;

    /// <summary>
    /// Extensions the plugin claims. XMP sidecars are first-class sources; RAW containers are TIFF based and
    /// carry the same XP/EXIF tags as ordinary TIFF files.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExtensions =
    [
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp", ".xmp", ".dng", ".cr2", ".nef", ".arw"
    ];

    public static readonly IReadOnlyList<string> ContentTypes =
        ["image/jpeg", "image/png", "image/tiff", "image/webp"];

    public static MediaMetadataSchema Schema { get; } = new(SchemaId, SchemaVersion,
    [
        new MediaMetadataField(Keywords, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
        new MediaMetadataField(HierarchicalKeywords, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
        new MediaMetadataField(Comment, MediaMetadataType.Text),
        new MediaMetadataField(Rating, MediaMetadataType.Integer)
    ]);
}
