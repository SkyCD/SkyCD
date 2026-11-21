using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Works.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the Microsoft Works plugin. Field keys
/// and units are part of the cached-data contract: changing the meaning or unit of a field requires a new
/// schema version, because the host keys stored results by schema id and version.
/// </summary>
public static class WorksSchema
{
    public const string MetadataSchemaId = "skycd.works.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string Container = "Container";
    public const string Storage = "Storage";
    public const string FormatVersion = "FormatVersion";

    public const string Title = "Title";
    public const string Subject = "Subject";
    public const string Author = "Author";
    public const string Keywords = "Keywords";
    public const string Comments = "Comments";
    public const string Template = "Template";
    public const string LastSavedBy = "LastSavedBy";
    public const string RevisionNumber = "RevisionNumber";
    public const string ApplicationName = "ApplicationName";

    public const string Category = "Category";
    public const string Manager = "Manager";
    public const string Company = "Company";
    public const string ContentStatus = "ContentStatus";
    public const string Language = "Language";

    public const string CreatedAt = "CreatedAt";
    public const string ModifiedAt = "ModifiedAt";
    public const string LastPrintedAt = "LastPrintedAt";

    public const string PageCount = "PageCount";
    public const string WordCount = "WordCount";
    public const string CharacterCount = "CharacterCount";

    public static readonly IReadOnlyList<string> Extensions = [".wps"];

    public static readonly IReadOnlyList<string> ContentTypes = ["application/vnd.ms-works"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Storage, MediaMetadataType.Text),
        new MediaMetadataField(FormatVersion, MediaMetadataType.Text),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Comments, MediaMetadataType.Text),
        new MediaMetadataField(Template, MediaMetadataType.Text),
        new MediaMetadataField(LastSavedBy, MediaMetadataType.Text),
        new MediaMetadataField(RevisionNumber, MediaMetadataType.Text),
        new MediaMetadataField(ApplicationName, MediaMetadataType.Text),
        new MediaMetadataField(Category, MediaMetadataType.Text),
        new MediaMetadataField(Manager, MediaMetadataType.Text),
        new MediaMetadataField(Company, MediaMetadataType.Text),
        new MediaMetadataField(ContentStatus, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(CreatedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(ModifiedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(LastPrintedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(PageCount, MediaMetadataType.Integer),
        new MediaMetadataField(WordCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer)
    ]);
}
