using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Text.Models;

/// <summary>
/// Stable schema identities, field keys and source types published by the text plugin. Field keys and units
/// are part of the cached-data contract: changing the meaning or unit of a field requires a new schema
/// version, because the host keys stored results by schema id and version.
/// </summary>
public static class TextSchema
{
    public const string MetadataSchemaId = "skycd.text.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.text.preview";
    public const int PreviewSchemaVersion = 1;

    /// <summary>Detected character encoding, for example <c>utf-8</c>, <c>utf-16le</c> or <c>iso-8859-1</c>.</summary>
    public const string Encoding = "Encoding";

    /// <summary>True when the source opened with a byte order mark.</summary>
    public const string HasByteOrderMark = "HasByteOrderMark";

    /// <summary>Dominant line ending: <c>lf</c>, <c>crlf</c>, <c>cr</c> or <c>mixed</c>.</summary>
    public const string LineEndings = "LineEndings";

    public const string LineCount = "LineCount";
    public const string CharacterCount = "CharacterCount";
    public const string ByteLength = "ByteLength";

    /// <summary>Allow-list category: <c>text</c>, <c>markdown</c>, <c>csv</c> or <c>source</c>.</summary>
    public const string TextKind = "TextKind";

    /// <summary>True when the whole source was read, false when scanning stopped at a bound.</summary>
    public const string ScanComplete = "ScanComplete";

    /// <summary>Bounded leading excerpt, plain text only.</summary>
    public const string TextExcerpt = "TextExcerpt";

    /// <summary>True when the excerpt is shorter than the text that was scanned.</summary>
    public const string ExcerptTruncated = "ExcerptTruncated";

    /// <summary>Markdown first-level heading, when the source is Markdown and has one.</summary>
    public const string Title = "Title";

    /// <summary>
    /// Extensions on the documented allow-list. Office documents are deliberately absent: this plugin does
    /// not parse compound or zipped document formats.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".txt", ".text", ".log", ".md", ".markdown", ".rst",
        ".csv", ".tsv", ".ini", ".cfg", ".conf", ".properties",
        ".c", ".h", ".cpp", ".hpp", ".cs", ".java", ".kt", ".py", ".rb", ".go", ".rs",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".css", ".scss",
        ".html", ".htm", ".xml", ".json", ".yaml", ".yml", ".toml",
        ".sh", ".bash", ".ps1", ".bat", ".sql", ".php"
    ];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "text/plain", "text/markdown", "text/csv", "text/tab-separated-values",
        "text/html", "text/xml", "text/css", "application/json", "application/xml",
        "application/x-yaml", "application/toml", "application/x-sh"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Encoding, MediaMetadataType.Text),
        new MediaMetadataField(HasByteOrderMark, MediaMetadataType.Boolean),
        new MediaMetadataField(LineEndings, MediaMetadataType.Text),
        new MediaMetadataField(LineCount, MediaMetadataType.Integer),
        new MediaMetadataField(CharacterCount, MediaMetadataType.Integer),
        new MediaMetadataField(ByteLength, MediaMetadataType.Integer, "byte"),
        new MediaMetadataField(TextKind, MediaMetadataType.Text),
        new MediaMetadataField(ScanComplete, MediaMetadataType.Boolean),
        new MediaMetadataField(TextExcerpt, MediaMetadataType.Text),
        new MediaMetadataField(ExcerptTruncated, MediaMetadataType.Boolean),
        new MediaMetadataField(Title, MediaMetadataType.Text)
    ]);

    /// <summary>Preview results carry the excerpt itself rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
