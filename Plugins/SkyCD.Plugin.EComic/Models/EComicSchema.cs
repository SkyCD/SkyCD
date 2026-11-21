using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.EComic.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the eComic plugin. Field keys and
/// units are part of the cached-data contract: changing the meaning of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class EComicSchema
{
    public const string MetadataSchemaId = "skycd.ecomic.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.ecomic.preview";
    public const int PreviewSchemaVersion = 1;

    /// <summary>The archive flavor detected from the bytes, for example <c>zip</c>, <c>rar</c> or <c>7z</c>.</summary>
    public const string Container = "Container";

    public const string Series = "Series";
    public const string Title = "Title";
    public const string Number = "Number";
    public const string Volume = "Volume";
    public const string Writer = "Writer";
    public const string Publisher = "Publisher";
    public const string Genre = "Genre";
    public const string Year = "Year";
    public const string PageCount = "PageCount";
    public const string LanguageIso = "LanguageISO";
    public const string Summary = "Summary";

    /// <summary>
    /// The name the catalog browser may show instead of the file name. It carries the series or title value
    /// the user selected, so switching the option never rewrites a catalogued file name.
    /// </summary>
    public const string PreferredName = "PreferredName";

    public static readonly IReadOnlyList<string> DefaultExtensions = [".cbz", ".cbr", ".cb7"];

    /// <summary>Comic-specific MIME types only; generic zip/rar/7z remain with the archive indexing plugins.</summary>
    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/vnd.comicbook+zip",
        "application/vnd.comicbook+rar",
        "application/vnd.comicbook",
        "application/x-cbz",
        "application/x-cbr",
        "application/x-cb7"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Series, MediaMetadataType.Text),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Number, MediaMetadataType.Text),
        new MediaMetadataField(Volume, MediaMetadataType.Text),
        new MediaMetadataField(Writer, MediaMetadataType.Text, Role: MediaMetadataFieldRole.Tags),
        new MediaMetadataField(Publisher, MediaMetadataType.Text),
        new MediaMetadataField(Genre, MediaMetadataType.Text),
        new MediaMetadataField(Year, MediaMetadataType.Integer),
        new MediaMetadataField(PageCount, MediaMetadataType.Integer),
        new MediaMetadataField(LanguageIso, MediaMetadataType.Text),
        new MediaMetadataField(Summary, MediaMetadataType.Text),
        new MediaMetadataField(PreferredName, MediaMetadataType.Text, Role: MediaMetadataFieldRole.PreferredName)
    ]);

    /// <summary>Preview results carry the cover image itself rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
