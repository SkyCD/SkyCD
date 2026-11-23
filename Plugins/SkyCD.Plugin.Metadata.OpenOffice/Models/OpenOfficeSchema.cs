using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.OpenOffice.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the OpenOffice plugin. Field keys and
/// units are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class OpenOfficeSchema
{
    public const string MetadataSchemaId = "skycd.openoffice.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.openoffice.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string DocumentType = "DocumentType";
    public const string FormatVersion = "FormatVersion";
    public const string IsEncrypted = "IsEncrypted";

    public const string Title = "Title";
    public const string Subject = "Subject";
    public const string Description = "Description";
    public const string Author = "Author";
    public const string InitialCreator = "InitialCreator";
    public const string Keywords = "Keywords";
    public const string Generator = "Generator";
    public const string Language = "Language";
    public const string Rights = "Rights";
    public const string Publisher = "Publisher";
    public const string Contributor = "Contributor";
    public const string Identifier = "Identifier";
    public const string Source = "Source";
    public const string Relation = "Relation";
    public const string Coverage = "Coverage";
    public const string Type = "Type";
    public const string Format = "Format";
    public const string Template = "Template";
    public const string PrintedBy = "PrintedBy";

    public const string CreatedAt = "CreatedAt";
    public const string ModifiedAt = "ModifiedAt";
    public const string PrintedAt = "PrintedAt";

    public const string EditingCycles = "EditingCycles";
    public const string EditingTimeMinutes = "EditingTimeMinutes";

    public const string PageCount = "PageCount";
    public const string TableCount = "TableCount";
    public const string ImageCount = "ImageCount";
    public const string ObjectCount = "ObjectCount";
    public const string ParagraphCount = "ParagraphCount";
    public const string WordCount = "WordCount";
    public const string CharacterCount = "CharacterCount";
    public const string CellCount = "CellCount";
    public const string SheetCount = "SheetCount";

    /// <summary>
    /// OpenDocument types and the legacy OpenOffice.org 1.x types. Formula templates are deliberately absent:
    /// their <c>.otf</c> suffix belongs to OpenType fonts, so those packages are selected by MIME type only.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".odt", ".ott", ".odm", ".otm", ".oth", ".ods", ".ots", ".odp", ".otp", ".odg", ".otg",
        ".odf", ".odc", ".odi", ".oti", ".odb",
        ".sxw", ".stw", ".sxg", ".sxc", ".stc", ".sxi", ".sti", ".sxd", ".std", ".sxm"
    ];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.text-template",
        "application/vnd.oasis.opendocument.text-master",
        "application/vnd.oasis.opendocument.text-master-template",
        "application/vnd.oasis.opendocument.text-web",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.spreadsheet-template",
        "application/vnd.oasis.opendocument.presentation",
        "application/vnd.oasis.opendocument.presentation-template",
        "application/vnd.oasis.opendocument.graphics",
        "application/vnd.oasis.opendocument.graphics-template",
        "application/vnd.oasis.opendocument.formula",
        "application/vnd.oasis.opendocument.formula-template",
        "application/vnd.oasis.opendocument.chart",
        "application/vnd.oasis.opendocument.chart-template",
        "application/vnd.oasis.opendocument.image",
        "application/vnd.oasis.opendocument.image-template",
        "application/vnd.oasis.opendocument.database",
        "application/vnd.sun.xml.writer",
        "application/vnd.sun.xml.writer.template",
        "application/vnd.sun.xml.writer.global",
        "application/vnd.sun.xml.calc",
        "application/vnd.sun.xml.calc.template",
        "application/vnd.sun.xml.impress",
        "application/vnd.sun.xml.impress.template",
        "application/vnd.sun.xml.draw",
        "application/vnd.sun.xml.draw.template",
        "application/vnd.sun.xml.math"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(DocumentType, MediaMetadataType.Text),
        new MediaMetadataField(FormatVersion, MediaMetadataType.Text),
        new MediaMetadataField(IsEncrypted, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Description, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(InitialCreator, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Generator, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(Rights, MediaMetadataType.Text),
        new MediaMetadataField(Publisher, MediaMetadataType.Text),
        new MediaMetadataField(Contributor, MediaMetadataType.Text),
        new MediaMetadataField(Identifier, MediaMetadataType.Text),
        new MediaMetadataField(Source, MediaMetadataType.Text),
        new MediaMetadataField(Relation, MediaMetadataType.Text),
        new MediaMetadataField(Coverage, MediaMetadataType.Text),
        new MediaMetadataField(Type, MediaMetadataType.Text),
        new MediaMetadataField(Format, MediaMetadataType.Text),
        new MediaMetadataField(Template, MediaMetadataType.Text),
        new MediaMetadataField(PrintedBy, MediaMetadataType.Text),
        new MediaMetadataField(CreatedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(ModifiedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(PrintedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(EditingCycles, MediaMetadataType.Integer),
        new MediaMetadataField(EditingTimeMinutes, MediaMetadataType.Integer, "min"),
        new MediaMetadataField(PageCount, MediaMetadataType.Integer),
        new MediaMetadataField(TableCount, MediaMetadataType.Integer),
        new MediaMetadataField(ImageCount, MediaMetadataType.Integer),
        new MediaMetadataField(ObjectCount, MediaMetadataType.Integer),
        new MediaMetadataField(ParagraphCount, MediaMetadataType.Integer),
        new MediaMetadataField(WordCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer),
        new MediaMetadataField(CellCount, MediaMetadataType.Integer),
        new MediaMetadataField(SheetCount, MediaMetadataType.Integer)
    ]);

    /// <summary>Preview results carry the embedded thumbnail rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
