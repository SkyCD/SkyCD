using System.Collections.Generic;

namespace SkyCD.Plugin.AbeMeda;

/// <summary>
/// One parsed catalog entry: a volume, folder or file element with its fields.
/// Indexes follow document (start-element) order so parents always precede children.
/// </summary>
public sealed record AbeMedaItem(
    int Index,
    AbeMedaElementKind Kind,
    int ParentIndex,
    bool HasItemChildren,
    bool HasThumbnail,
    Dictionary<AbeMedaField, string> Fields,
    Dictionary<string, string> ExtraFields);

/// <summary>
/// Result of parsing an abeMeda/NeoFinder-family XML export.
/// </summary>
public sealed record AbeMedaParseResult(
    string? RootElement,
    string? SchemaVersion,
    IReadOnlyList<AbeMedaItem?> Items,
    IReadOnlyList<string> UnsupportedFields,
    int SkippedItemsCount,
    IReadOnlyList<string> SkippedReasons);
