using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.IWork.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the Apple iWork plugin.
/// </summary>
/// <remarks>
/// Field keys and units are part of the cached-data contract: changing the meaning or unit of a field requires a
/// new schema version, because the host keys stored results by schema id and version.
/// </remarks>
public static class IWorkSchema
{
    public const string MetadataSchemaId = "skycd.iwork.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.iwork.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string DocumentType = "DocumentType";
    public const string IsPasswordProtected = "IsPasswordProtected";
    public const string FormatVersion = "FormatVersion";
    public const string ProducerBuild = "ProducerBuild";
    public const string DocumentIdentifier = "DocumentIdentifier";
    public const string Language = "Language";
    public const string Locale = "Locale";
    public const string IsMultiPage = "IsMultiPage";
    public const string HasExternalData = "HasExternalData";
    public const string HasPreview = "HasPreview";
    public const string PreviewWidth = "PreviewWidth";
    public const string PreviewHeight = "PreviewHeight";
    public const string DataAssetCount = "DataAssetCount";
    public const string IwaComponentCount = "IwaComponentCount";
    public const string SlideCount = "SlideCount";

    public static readonly IReadOnlyList<string> Extensions = [".pages", ".numbers", ".key"];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/x-iwork-pages-sffpages",
        "application/x-iwork-numbers-sffnumbers",
        "application/x-iwork-keynote-sffkey"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(DocumentType, MediaMetadataType.Text),
        new MediaMetadataField(IsPasswordProtected, MediaMetadataType.Boolean),
        new MediaMetadataField(FormatVersion, MediaMetadataType.Text),
        new MediaMetadataField(ProducerBuild, MediaMetadataType.Text),
        new MediaMetadataField(DocumentIdentifier, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(Locale, MediaMetadataType.Text),
        new MediaMetadataField(IsMultiPage, MediaMetadataType.Boolean),
        new MediaMetadataField(HasExternalData, MediaMetadataType.Boolean),
        new MediaMetadataField(HasPreview, MediaMetadataType.Boolean),
        new MediaMetadataField(PreviewWidth, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(PreviewHeight, MediaMetadataType.Integer, "px"),
        new MediaMetadataField(DataAssetCount, MediaMetadataType.Integer),
        new MediaMetadataField(IwaComponentCount, MediaMetadataType.Integer),
        new MediaMetadataField(SlideCount, MediaMetadataType.Integer)
    ]);

    /// <summary>Preview results carry the embedded thumbnail rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
