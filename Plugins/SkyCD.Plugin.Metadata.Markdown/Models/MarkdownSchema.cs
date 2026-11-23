using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Markdown.Models;

/// <summary>
/// Stable schema identity, field keys and source types published by the Markdown metadata plugin. Field keys
/// are part of the cached-data contract: changing the meaning of a field requires a new schema version,
/// because the host keys stored results by schema id and version.
/// </summary>
public static class MarkdownSchema
{
    public const string MetadataSchemaId = "skycd.markdown.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.markdown.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string HasFrontMatter = "HasFrontMatter";
    public const string FrontMatterValid = "FrontMatterValid";

    public const string Title = "Title";
    public const string Subject = "Subject";
    public const string Author = "Author";
    public const string Keywords = "Keywords";
    public const string Category = "Category";
    public const string Language = "Language";
    public const string PublishedAt = "PublishedAt";

    public const string FirstHeading = "FirstHeading";
    public const string HeadingCount = "HeadingCount";
    public const string WordCount = "WordCount";
    public const string CharacterCount = "CharacterCount";
    public const string LinkCount = "LinkCount";
    public const string ImageCount = "ImageCount";
    public const string CodeBlockCount = "CodeBlockCount";

    /// <summary>Bounded leading plain-text excerpt.</summary>
    public const string TextExcerpt = "TextExcerpt";

    /// <summary>True when the excerpt was cut short by the configured bound.</summary>
    public const string TextExcerptTruncated = "TextExcerptTruncated";

    public static readonly IReadOnlyList<string> Extensions = [".md", ".markdown", ".mdown", ".mkd"];

    public static readonly IReadOnlyList<string> ContentTypes = ["text/markdown", "text/x-markdown"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(HasFrontMatter, MediaMetadataType.Boolean),
        new MediaMetadataField(FrontMatterValid, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Category, MediaMetadataType.Text),
        new MediaMetadataField(Language, MediaMetadataType.Text),
        new MediaMetadataField(PublishedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(FirstHeading, MediaMetadataType.Text),
        new MediaMetadataField(HeadingCount, MediaMetadataType.Integer),
        new MediaMetadataField(WordCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer),
        new MediaMetadataField(LinkCount, MediaMetadataType.Integer),
        new MediaMetadataField(ImageCount, MediaMetadataType.Integer),
        new MediaMetadataField(CodeBlockCount, MediaMetadataType.Integer),
        new MediaMetadataField(TextExcerpt, MediaMetadataType.Text),
        new MediaMetadataField(TextExcerptTruncated, MediaMetadataType.Boolean)
    ]);

    /// <summary>Preview results carry the rendered page image rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
