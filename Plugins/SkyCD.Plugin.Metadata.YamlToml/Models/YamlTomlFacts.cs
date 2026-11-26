using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.YamlToml.Models;

/// <summary>Structure collected from one document, plus the lines of its first page.</summary>
internal sealed class YamlTomlFacts
{
    public required string Format { get; init; }

    public required bool Valid { get; init; }

    public required long ByteLength { get; init; }

    public string? ErrorCode { get; init; }

    public int? ErrorLine { get; init; }

    public int? ErrorColumn { get; init; }

    public string? FormatVersion { get; init; }

    public int DocumentCount { get; init; }

    public int MappingCount { get; init; }

    public int SequenceCount { get; init; }

    public int ScalarCount { get; init; }

    public int MaxDepth { get; init; }

    public int AnchorCount { get; init; }

    public int AliasCount { get; init; }

    public int MergeKeyCount { get; init; }

    public int CommentCount { get; init; }

    public int DuplicateKeyCount { get; init; }

    public int TableCount { get; init; }

    public int ArrayOfTablesCount { get; init; }

    public int KeyCount { get; init; }

    public IReadOnlyList<string> TopLevelKeys { get; init; } = [];

    public IReadOnlyList<string> TopLevelTables { get; init; } = [];

    public IReadOnlyList<YamlTomlPreviewLine> Lines { get; init; } = [];
}
