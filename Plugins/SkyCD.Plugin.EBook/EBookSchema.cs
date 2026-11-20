using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Stable schema identities, field keys and source types published by the eBook plugin. Field keys and units
/// are part of the cached-data contract: changing the meaning of a field requires a new schema version,
/// because the host keys stored results by schema id and version.
/// </summary>
public static class EBookSchema
{
    public const string MetadataSchemaId = "skycd.ebook.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.ebook.preview";
    public const int PreviewSchemaVersion = 1;

    /// <summary>The physical container detected from the bytes: <c>zip</c>, <c>palmdb</c> or <c>xml</c>.</summary>
    public const string Container = "Container";

    /// <summary>
    /// The logical book format: <c>epub</c>, <c>ibook</c>, <c>mobi</c>, <c>kf8</c>, <c>palmdoc</c> or
    /// <c>fb2</c>.
    /// </summary>
    public const string Format = "Format";

    public const string Title = "Title";

    /// <summary>Every author of the book, separated by semicolons so the host can create one tag per name.</summary>
    public const string Authors = "Authors";

    public const string Publisher = "Publisher";
    public const string Language = "Language";
    public const string Subject = "Subject";
    public const string Description = "Description";

    /// <summary>The first identifier the book carries, such as an ISBN, an ASIN or a package unique id.</summary>
    public const string Identifier = "Identifier";

    public const string PublishedAt = "PublishedAt";

    /// <summary>
    /// The publication year. Books that carry only a year — most FictionBook documents — publish it here
    /// instead of inventing a month and day for <see cref="PublishedAt"/>.
    /// </summary>
    public const string PublishedYear = "PublishedYear";

    /// <summary>Published only for a book whose content is encrypted, never as <c>false</c>.</summary>
    public const string Protected = "Protected";

    /// <summary>
    /// The name the catalog browser may show instead of the file name. It carries the author or title value
    /// the user selected, so switching the option never rewrites a catalogued file name.
    /// </summary>
    public const string PreferredName = "PreferredName";

    public static readonly IReadOnlyList<string> DefaultExtensions =
        [".epub", ".ibook", ".mobi", ".prc", ".azw", ".azw3", ".fb2"];

    /// <summary>Book-specific MIME types only; generic zip and XML remain with the archive indexing plugins.</summary>
    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/epub+zip",
        "application/x-ibooks+zip",
        "application/x-mobipocket-ebook",
        "application/vnd.amazon.ebook",
        "application/x-mobi8-ebook",
        "application/x-fictionbook+xml",
        "application/x-fictionbook"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Format, MediaMetadataType.Text),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Authors, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
        new MediaMetadataField(Publisher, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Description, MediaMetadataType.Text),
        new MediaMetadataField(Identifier, MediaMetadataType.Text),
        new MediaMetadataField(PublishedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(PublishedYear, MediaMetadataType.Integer),
        new MediaMetadataField(Protected, MediaMetadataType.Boolean),
        new MediaMetadataField(PreferredName, MediaMetadataType.Text, Role: MediaMetadataFieldRole.PreferredName)
    ]);

    /// <summary>Preview results carry the cover image itself rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
