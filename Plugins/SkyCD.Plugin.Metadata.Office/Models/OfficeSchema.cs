using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Office.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the Microsoft Office plugin. Field keys
/// and units are part of the cached-data contract: changing the meaning or unit of a field requires a new
/// schema version, because the host keys stored results by schema id and version.
/// </summary>
public static class OfficeSchema
{
    public const string MetadataSchemaId = "skycd.office.metadata";

    /// <summary>
    /// Version 2 widens the family from Word, Excel and PowerPoint to Visio, OneNote, Access and Project and
    /// adds the count and profile fields those applications publish. Every version 1 key keeps its meaning,
    /// so a version 1 result for a Word, Excel or PowerPoint document stays comparable.
    /// </summary>
    public const int MetadataSchemaVersion = 2;

    public const string PreviewSchemaId = "skycd.office.preview";
    public const int PreviewSchemaVersion = 1;

    /// <summary>
    /// How the document is stored: <c>ooxml</c> for a ZIP package, <c>ole</c> for a compound file,
    /// <c>onenote</c> for a OneNote revision store, <c>cab</c> for a OneNote notebook package, <c>jet</c> for
    /// an Access database and <c>text</c> for Project's delimited interchange format.
    /// </summary>
    public const string Container = "Container";

    /// <summary>
    /// Which Office application wrote the document: <c>word</c>, <c>excel</c>, <c>powerpoint</c>, <c>visio</c>,
    /// <c>onenote</c>, <c>access</c> or <c>project</c>.
    /// </summary>
    public const string DocumentType = "DocumentType";

    /// <summary>True only when a protection wrapper is present and the content was therefore left unread.</summary>
    public const string IsEncrypted = "IsEncrypted";

    /// <summary>Why the content was left unread, as the reading library reported it.</summary>
    public const string ProtectionReason = "ProtectionReason";

    /// <summary>True when the source is a template or a stencil rather than an ordinary document.</summary>
    public const string IsTemplate = "IsTemplate";

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

    /// <summary>Worksheets in a workbook, or pages in a OneNote section.</summary>
    public const string SheetCount = "SheetCount";

    /// <summary>Sections listed by a OneNote table of contents or notebook package.</summary>
    public const string SectionCount = "SectionCount";

    /// <summary>Newline-separated page or section titles, in the order the document lists them.</summary>
    public const string PageTitles = "PageTitles";

    /// <summary>User tables in an Access database.</summary>
    public const string TableCount = "TableCount";

    /// <summary>Saved queries in an Access database.</summary>
    public const string QueryCount = "QueryCount";

    /// <summary>Tasks in a project, including the tasks nested inside summary tasks.</summary>
    public const string TaskCount = "TaskCount";

    /// <summary>Tasks directly under the project, which is the visible first level of the task tree.</summary>
    public const string TopLevelTaskCount = "TopLevelTaskCount";

    public const string PlanStartAt = "PlanStartAt";
    public const string PlanFinishAt = "PlanFinishAt";

    /// <summary>The concrete file profile the container was read as, such as <c>xlsb</c> or <c>Ace12</c>.</summary>
    public const string FormatProfile = "FormatProfile";

    public static readonly IReadOnlyList<string> Extensions =
    [
        ".docx", ".docm", ".dotx", ".dotm", ".doc", ".dot",
        ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls", ".xlt",
        ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm", ".ppt", ".pot", ".pps",
        ".vsdx", ".vsdm", ".vstx", ".vstm", ".vssx", ".vssm", ".vsd", ".vss", ".vst",
        ".one", ".onetoc2", ".onepkg",
        ".mdb", ".accdb",
        ".mpp", ".mpt", ".mpx"
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
        "application/vnd.ms-powerpoint",
        "application/vnd.ms-visio.drawing",
        "application/vnd.ms-visio.drawing.macroEnabled.12",
        "application/vnd.ms-visio.template",
        "application/vnd.ms-visio.template.macroEnabled.12",
        "application/vnd.ms-visio.stencil",
        "application/vnd.ms-visio.stencil.macroEnabled.12",
        "application/vnd.visio",
        "application/onenote",
        "application/msonenote",
        "application/vnd.ms-access",
        "application/x-msaccess",
        "application/vnd.ms-project"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(DocumentType, MediaMetadataType.Text),
        new MediaMetadataField(IsEncrypted, MediaMetadataType.Boolean),
        new MediaMetadataField(ProtectionReason, MediaMetadataType.Text),
        new MediaMetadataField(IsTemplate, MediaMetadataType.Boolean),
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
        new MediaMetadataField(SlideCount, MediaMetadataType.Integer),
        new MediaMetadataField(SheetCount, MediaMetadataType.Integer),
        new MediaMetadataField(SectionCount, MediaMetadataType.Integer),
        new MediaMetadataField(PageTitles, MediaMetadataType.Text),
        new MediaMetadataField(TableCount, MediaMetadataType.Integer),
        new MediaMetadataField(QueryCount, MediaMetadataType.Integer),
        new MediaMetadataField(TaskCount, MediaMetadataType.Integer),
        new MediaMetadataField(TopLevelTaskCount, MediaMetadataType.Integer),
        new MediaMetadataField(PlanStartAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(PlanFinishAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(FormatProfile, MediaMetadataType.Text)
    ]);

    /// <summary>Preview results carry the embedded thumbnail rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
