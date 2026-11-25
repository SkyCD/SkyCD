using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Pdf.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the PDF plugin. Field keys and units
/// are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class PdfSchema
{
    public const string MetadataSchemaId = "skycd.pdf.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.pdf.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Container = "Container";
    public const string PageCount = "PageCount";
    public const string PdfVersion = "PdfVersion";
    public const string PageWidth = "PageWidth";
    public const string PageHeight = "PageHeight";
    public const string IsEncrypted = "IsEncrypted";

    public const string Title = "Title";
    public const string Author = "Author";
    public const string Subject = "Subject";
    public const string Keywords = "Keywords";
    public const string Creator = "Creator";
    public const string Producer = "Producer";
    public const string CreatedAt = "CreatedAt";
    public const string ModifiedAt = "ModifiedAt";

    /// <summary>Bounded text excerpt of the leading pages, plain text only.</summary>
    public const string TextExcerpt = "TextExcerpt";

    /// <summary>True when the excerpt was cut short by the configured bound.</summary>
    public const string TextExcerptTruncated = "TextExcerptTruncated";

    public static readonly IReadOnlyList<string> Extensions = [".pdf"];

    public static readonly IReadOnlyList<string> ContentTypes = ["application/pdf"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Container, MediaMetadataType.Text),
        new MediaMetadataField(PageCount, MediaMetadataType.Integer),
        new MediaMetadataField(PdfVersion, MediaMetadataType.Number),
        new MediaMetadataField(PageWidth, MediaMetadataType.Number, "pt"),
        new MediaMetadataField(PageHeight, MediaMetadataType.Number, "pt"),
        new MediaMetadataField(IsEncrypted, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text),
        new MediaMetadataField(Author, MediaMetadataType.Text),
        new MediaMetadataField(Subject, MediaMetadataType.Text),
        new MediaMetadataField(Keywords, MediaMetadataType.Text),
        new MediaMetadataField(Creator, MediaMetadataType.Text),
        new MediaMetadataField(Producer, MediaMetadataType.Text),
        new MediaMetadataField(CreatedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(ModifiedAt, MediaMetadataType.Timestamp),
        new MediaMetadataField(TextExcerpt, MediaMetadataType.Text),
        new MediaMetadataField(TextExcerptTruncated, MediaMetadataType.Boolean)
    ]);

    /// <summary>Preview results carry the rendered page rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
