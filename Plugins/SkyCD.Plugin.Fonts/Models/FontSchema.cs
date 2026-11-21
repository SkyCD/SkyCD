using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Fonts.Models;

/// <summary>
/// Stable schema identities and field keys of the font plugin. Field keys are part of the cached-data
/// contract: changing the meaning of a field requires a new schema version, because the host keys stored
/// results by schema id and version.
/// </summary>
public static class FontSchema
{
    public const string MetadataSchemaId = "skycd.font.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.font.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Family = "Family";
    public const string Subfamily = "Subfamily";
    public const string FullName = "FullName";
    public const string PostScriptName = "PostScriptName";
    public const string Version = "Version";
    public const string Copyright = "Copyright";

    /// <summary>Number of faces in a ttc or otc collection; omitted for a single-face file.</summary>
    public const string FaceCount = "FaceCount";

    public static readonly IReadOnlyList<string> DefaultExtensions = [".ttf", ".ttc", ".otf", ".otc"];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "font/ttf",
        "font/otf",
        "font/collection",
        "application/font-sfnt",
        "application/x-font-ttf",
        "application/x-font-opentype"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Family, MediaMetadataType.Text),
        new MediaMetadataField(Subfamily, MediaMetadataType.Text),
        new MediaMetadataField(FullName, MediaMetadataType.Text),
        new MediaMetadataField(PostScriptName, MediaMetadataType.Text),
        new MediaMetadataField(Version, MediaMetadataType.Text),
        new MediaMetadataField(Copyright, MediaMetadataType.Text),
        new MediaMetadataField(FaceCount, MediaMetadataType.Integer)
    ]);

    /// <summary>Preview results carry the rendered image rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
