using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Html.Models;

/// <summary>
/// Stable schema identity, field keys and source types published by the HTML metadata plugin. Field keys
/// are part of the cached-data contract: changing the meaning of a field requires a new schema version,
/// because the host keys stored results by schema id and version.
/// </summary>
public static class HtmlSchema
{
    public const string MetadataSchemaId = "skycd.html.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.html.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Keywords = "Keywords";
    public const string Author = "Author";
    public const string Generator = "Generator";
    public const string Language = "Language";
    public const string DeclaredCharset = "DeclaredCharset";

    /// <summary>True when the document carries a <c>base</c> element with an <c>href</c> attribute.</summary>
    public const string HasBaseHref = "HasBaseHref";

    public const string LinkCount = "LinkCount";
    public const string ImageCount = "ImageCount";
    public const string ScriptCount = "ScriptCount";
    public const string WordCount = "WordCount";
    public const string CharacterCount = "CharacterCount";

    /// <summary>Bounded leading plain-text excerpt.</summary>
    public const string TextExcerpt = "TextExcerpt";

    /// <summary>True when the excerpt was cut short by the configured bound.</summary>
    public const string TextExcerptTruncated = "TextExcerptTruncated";

    public static readonly IReadOnlyList<string> Extensions = [".html", ".htm"];

    public static readonly IReadOnlyList<string> ContentTypes = ["text/html"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Description, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Generator, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(DeclaredCharset, MediaMetadataType.Text),
        new MediaMetadataField(HasBaseHref, MediaMetadataType.Boolean),
        new MediaMetadataField(LinkCount, MediaMetadataType.Integer),
        new MediaMetadataField(ImageCount, MediaMetadataType.Integer),
        new MediaMetadataField(ScriptCount, MediaMetadataType.Integer),
        new MediaMetadataField(WordCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer),
        new MediaMetadataField(TextExcerpt, MediaMetadataType.Text),
        new MediaMetadataField(TextExcerptTruncated, MediaMetadataType.Boolean)
    ]);

    /// <summary>Preview results carry the rendered page image rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
