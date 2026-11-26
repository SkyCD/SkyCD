using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.YamlToml.Models;

/// <summary>
/// Stable schema identity and field keys for YAML and TOML documents. Keys are part of the cached-data
/// contract: changing a field's meaning requires a new schema version.
/// </summary>
public static class YamlTomlSchema
{
    public const string MetadataSchemaId = "skycd.yamltoml.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.yamltoml.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Format = "Format";
    public const string Valid = "Valid";
    public const string ByteLength = "ByteLength";

    public const string ErrorCode = "ErrorCode";
    public const string ErrorLine = "ErrorLine";
    public const string ErrorColumn = "ErrorColumn";

    public const string FormatVersion = "FormatVersion";
    public const string DocumentCount = "DocumentCount";
    public const string MappingCount = "MappingCount";
    public const string SequenceCount = "SequenceCount";
    public const string ScalarCount = "ScalarCount";
    public const string MaxDepth = "MaxDepth";
    public const string AnchorCount = "AnchorCount";
    public const string AliasCount = "AliasCount";
    public const string MergeKeyCount = "MergeKeyCount";
    public const string CommentCount = "CommentCount";
    public const string DuplicateKeyCount = "DuplicateKeyCount";
    public const string TopLevelKeys = "TopLevelKeys";

    public const string TableCount = "TableCount";
    public const string ArrayOfTablesCount = "ArrayOfTablesCount";
    public const string KeyCount = "KeyCount";
    public const string TopLevelTables = "TopLevelTables";

    public static readonly IReadOnlyList<string> Extensions = [".yaml", ".yml", ".toml"];

    public static readonly IReadOnlyList<string> ContentTypes =
    [
        "application/yaml",
        "text/yaml",
        "text/x-yaml",
        "application/toml",
        "text/toml",
        "application/x-toml"
    ];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Format, MediaMetadataType.Text),
        new MediaMetadataField(Valid, MediaMetadataType.Boolean),
        new MediaMetadataField(ByteLength, MediaMetadataType.Integer),
        new MediaMetadataField(ErrorCode, MediaMetadataType.Text),
        new MediaMetadataField(ErrorLine, MediaMetadataType.Integer),
        new MediaMetadataField(ErrorColumn, MediaMetadataType.Integer),
        new MediaMetadataField(FormatVersion, MediaMetadataType.Text),
        new MediaMetadataField(DocumentCount, MediaMetadataType.Integer),
        new MediaMetadataField(MappingCount, MediaMetadataType.Integer),
        new MediaMetadataField(SequenceCount, MediaMetadataType.Integer),
        new MediaMetadataField(ScalarCount, MediaMetadataType.Integer),
        new MediaMetadataField(MaxDepth, MediaMetadataType.Integer),
        new MediaMetadataField(AnchorCount, MediaMetadataType.Integer),
        new MediaMetadataField(AliasCount, MediaMetadataType.Integer),
        new MediaMetadataField(MergeKeyCount, MediaMetadataType.Integer),
        new MediaMetadataField(CommentCount, MediaMetadataType.Integer),
        new MediaMetadataField(DuplicateKeyCount, MediaMetadataType.Integer),
        new MediaMetadataField(TopLevelKeys, MediaMetadataType.Text),
        new MediaMetadataField(TableCount, MediaMetadataType.Integer),
        new MediaMetadataField(ArrayOfTablesCount, MediaMetadataType.Integer),
        new MediaMetadataField(KeyCount, MediaMetadataType.Integer),
        new MediaMetadataField(TopLevelTables, MediaMetadataType.Text)
    ]);

    /// <summary>Preview results carry the rendered page image rather than values.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
