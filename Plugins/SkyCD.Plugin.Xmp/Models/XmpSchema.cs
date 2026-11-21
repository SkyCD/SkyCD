using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Xmp.Models;

/// <summary>
/// Stable schema identity, field keys and claimed source types published by the XMP plugin. Field keys and
/// their meaning are part of the cached-data contract, because the host keys stored results by schema id and
/// version.
/// </summary>
public static class XmpSchema
{
    public const string SchemaId = "skycd.xmp.metadata";
    public const int SchemaVersion = 1;

    public const string Tags = "Tags";
    public const string Comment = "Comment";
    public const string Rating = "Rating";

    /// <summary>Standalone XMP sidecars, as written by Lightroom, Bridge, digiKam and similar tools.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".xmp"];

    public static readonly IReadOnlyList<string> ContentTypes = ["application/rdf+xml"];

    public static MediaMetadataSchema Schema { get; } =
        new(SchemaId, SchemaVersion,
        [
            new MediaMetadataField(Tags, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
            new MediaMetadataField(Comment, MediaMetadataType.Text),
            new MediaMetadataField(Rating, MediaMetadataType.Integer)
        ]);
}
