using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Rtf.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the RTF plugin. Field keys and units
/// are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class RtfSchema
{
    public const string MetadataSchemaId = "skycd.rtf.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.rtf.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string RtfVersion = "RtfVersion";
    public const string AnsiCodePage = "AnsiCodePage";
    public const string IsProtected = "IsProtected";

    public const string Title = "Title";
    public const string Subject = "Subject";
    public const string Author = "Author";
    public const string Manager = "Manager";
    public const string Company = "Company";
    public const string Operator = "Operator";
    public const string Category = "Category";
    public const string Keywords = "Keywords";
    public const string Comment = "Comment";
    public const string RevisionNumber = "RevisionNumber";

    public const string EditingTimeMinutes = "EditingTimeMinutes";
    public const string PageCount = "PageCount";
    public const string WordCount = "WordCount";
    public const string CharacterCount = "CharacterCount";
    public const string CharacterCountWithSpaces = "CharacterCountWithSpaces";
    public const string ParagraphCount = "ParagraphCount";

    public const string CreatedAt = "CreatedAt";
    public const string ModifiedAt = "ModifiedAt";
    public const string LastPrintedAt = "LastPrintedAt";

    /// <summary>Bounded leading plain-text excerpt.</summary>
    public const string TextExcerpt = "TextExcerpt";

    /// <summary>True when the excerpt was cut short by the configured bound.</summary>
    public const string TextExcerptTruncated = "TextExcerptTruncated";

    public static readonly IReadOnlyList<string> Extensions = [".rtf"];

    public static readonly IReadOnlyList<string> ContentTypes = ["application/rtf", "text/rtf"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(RtfVersion, MediaMetadataType.Integer),
        new MediaMetadataField(AnsiCodePage, MediaMetadataType.Integer),
        new MediaMetadataField(IsProtected, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Manager, MediaMetadataType.Text),
        new MediaMetadataField(Company, MediaMetadataType.Text),
        new MediaMetadataField(Operator, MediaMetadataType.Text),
        new MediaMetadataField(Category, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Comment, MediaMetadataType.Text),
        new MediaMetadataField(RevisionNumber, MediaMetadataType.Integer),
        new MediaMetadataField(EditingTimeMinutes, MediaMetadataType.Integer, "min"),
        new MediaMetadataField(PageCount, MediaMetadataType.Integer),
        new MediaMetadataField(WordCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCountWithSpaces, MediaMetadataType.Integer),
        new MediaMetadataField(ParagraphCount, MediaMetadataType.Integer),
        new MediaMetadataField(CreatedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(ModifiedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(LastPrintedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(TextExcerpt, MediaMetadataType.Text),
        new MediaMetadataField(TextExcerptTruncated, MediaMetadataType.Boolean)
    ]);

    /// <summary>Preview results carry the rendered page rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
