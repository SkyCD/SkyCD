using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Office.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the Microsoft Office plugin. Field keys
/// and units are part of the cached-data contract: changing the meaning or unit of a field requires a new
/// schema version, because the host keys stored results by schema id and version.
/// </summary>
public static class OfficeSchema
{
    public const string MetadataSchemaId = "skycd.office.metadata";
    public const int MetadataSchemaVersion = 1;

    /// <summary>How the document is stored: <c>ooxml</c> for a ZIP package, <c>ole</c> for a compound file.</summary>
    public const string Container = "Container";

    /// <summary>Which Office application wrote the document: <c>word</c>, <c>excel</c> or <c>powerpoint</c>.</summary>
    public const string DocumentType = "DocumentType";

    /// <summary>True only when an encryption wrapper is present and was therefore left unread.</summary>
    public const string IsEncrypted = "IsEncrypted";

    public const string Title = "Title";
    public const string Subject = "Subject";
    public const string Author = "Author";
    public const string Keywords = "Keywords";
    public const string Comments = "Comments";
    public const string Category = "Category";
    public const string ContentStatus = "ContentStatus";
    public const string Template = "Template";
    public const string LastSavedBy = "LastSavedBy";
    public const string RevisionNumber = "RevisionNumber";
    public const string Language = "Language";

    public const string ApplicationName = "ApplicationName";
    public const string ApplicationVersion = "ApplicationVersion";
    public const string Company = "Company";
    public const string Manager = "Manager";

    public const string CreatedAt = "CreatedAt";
    public const string ModifiedAt = "ModifiedAt";
    public const string LastPrintedAt = "LastPrintedAt";

    public const string PageCount = "PageCount";
    public const string WordCount = "WordCount";
    public const string CharacterCount = "CharacterCount";
    public const string SlideCount = "SlideCount";

    public static readonly IReadOnlyList<string> Extensions =
    [
        ".docx", ".docm", ".dotx", ".dotm", ".doc",
        ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls",
        ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm", ".ppt"
    ];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template",
        "application/vnd.ms-word.document.macroEnabled.12",
        "application/vnd.ms-word.template.macroEnabled.12",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template",
        "application/vnd.ms-excel.sheet.macroEnabled.12",
        "application/vnd.ms-excel.template.macroEnabled.12",
        "application/vnd.ms-excel.sheet.binary.macroEnabled.12",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.presentationml.template",
        "application/vnd.openxmlformats-officedocument.presentationml.slideshow",
        "application/vnd.ms-powerpoint.presentation.macroEnabled.12",
        "application/vnd.ms-powerpoint.template.macroEnabled.12",
        "application/vnd.ms-powerpoint.slideshow.macroEnabled.12",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(DocumentType, MediaMetadataType.Text),
        new MediaMetadataField(IsEncrypted, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Comments, MediaMetadataType.Text),
        new MediaMetadataField(Category, MediaMetadataType.Text),
        new MediaMetadataField(ContentStatus, MediaMetadataType.Text),
        new MediaMetadataField(Template, MediaMetadataType.Text),
        new MediaMetadataField(LastSavedBy, MediaMetadataType.Text),
        new MediaMetadataField(RevisionNumber, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(ApplicationName, MediaMetadataType.Text),
        new MediaMetadataField(ApplicationVersion, MediaMetadataType.Text),
        new MediaMetadataField(Company, MediaMetadataType.Text),
        new MediaMetadataField(Manager, MediaMetadataType.Text),
        new MediaMetadataField(CreatedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(ModifiedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(LastPrintedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(PageCount, MediaMetadataType.Integer),
        new MediaMetadataField(WordCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer),
        new MediaMetadataField(SlideCount, MediaMetadataType.Integer)
    ]);
}
