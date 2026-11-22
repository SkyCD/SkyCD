using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.WordPerfect.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the WordPerfect plugin. Field keys and
/// units are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class WordPerfectSchema
{
    public const string MetadataSchemaId = "skycd.wordperfect.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string Container = "Container";
    public const string Storage = "Storage";
    public const string FormatVersion = "FormatVersion";
    public const string IsEncrypted = "IsEncrypted";

    public const string Title = "Title";
    public const string Subject = "Subject";
    public const string Author = "Author";
    public const string Keywords = "Keywords";
    public const string Category = "Category";
    public const string Abstract = "Abstract";
    public const string Comments = "Comments";
    public const string Office = "Office";
    public const string Department = "Department";
    public const string Division = "Division";
    public const string Owner = "Owner";
    public const string Project = "Project";
    public const string Publisher = "Publisher";
    public const string Language = "Language";
    public const string Source = "Source";
    public const string Status = "Status";
    public const string Security = "Security";
    public const string Purpose = "Purpose";
    public const string RevisionNumber = "RevisionNumber";
    public const string VersionNumber = "VersionNumber";
    public const string DocumentNumber = "DocumentNumber";

    public const string CreatedAt = "CreatedAt";
    public const string CompletedAt = "CompletedAt";
    public const string RecordedAt = "RecordedAt";
    public const string ModifiedAt = "ModifiedAt";
    public const string VersionDate = "VersionDate";

    public static readonly IReadOnlyList<string> Extensions = [".wpd", ".wpt"];

    public static readonly IReadOnlyList<string> ContentTypes = ["application/vnd.wordperfect"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Storage, MediaMetadataType.Text),
        new MediaMetadataField(FormatVersion, MediaMetadataType.Text),
        new MediaMetadataField(IsEncrypted, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Category, MediaMetadataType.Text),
        new MediaMetadataField(Abstract, MediaMetadataType.Text),
        new MediaMetadataField(Comments, MediaMetadataType.Text),
        new MediaMetadataField(Office, MediaMetadataType.Text),
        new MediaMetadataField(Department, MediaMetadataType.Text),
        new MediaMetadataField(Division, MediaMetadataType.Text),
        new MediaMetadataField(Owner, MediaMetadataType.Text),
        new MediaMetadataField(Project, MediaMetadataType.Text),
        new MediaMetadataField(Publisher, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(Source, MediaMetadataType.Text),
        new MediaMetadataField(Status, MediaMetadataType.Text),
        new MediaMetadataField(Security, MediaMetadataType.Text),
        new MediaMetadataField(Purpose, MediaMetadataType.Text),
        new MediaMetadataField(RevisionNumber, MediaMetadataType.Text),
        new MediaMetadataField(VersionNumber, MediaMetadataType.Text),
        new MediaMetadataField(DocumentNumber, MediaMetadataType.Text),
        new MediaMetadataField(CreatedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(CompletedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(RecordedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(ModifiedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(VersionDate, MediaMetadataType.Timestamp)
    ]);

    /// <summary>Extended document summary tags that carry display text, in publication order.</summary>
    internal static readonly (ushort Tag, string Key)[] TextFields =
    [
        (17, Title),
        (46, Subject),
        (5, Author),
        (26, Keywords),
        (10, Category),
        (1, Abstract),
        (13, Comments),
        (30, Office),
        (16, Department),
        (21, Division),
        (31, Owner),
        (32, Project),
        (33, Publisher),
        (27, Language),
        (44, Source),
        (45, Status),
        (43, Security),
        (34, Purpose),
        (41, RevisionNumber),
        (51, VersionNumber),
        (22, DocumentNumber)
    ];

    /// <summary>Extended document summary tags that carry a ten-byte date, in publication order.</summary>
    internal static readonly (ushort Tag, string Key)[] DateFields =
    [
        (14, CreatedAt),
        (15, CompletedAt),
        (37, RecordedAt),
        (39, ModifiedAt),
        (49, VersionDate)
    ];
}
