using System.Collections.Generic;

namespace SkyCD.Plugin.GCstar;

/// <summary>
/// One parsed collection item. Indexes follow document order.
/// </summary>
public sealed record GCstarItem(
    int Index,
    Dictionary<string, string> Fields,
    Dictionary<string, string> ExtraFields);

/// <summary>
/// Result of parsing a GCstar collection export.
/// </summary>
public sealed record GCstarParseResult(
    string? CollectionType,
    string? Version,
    string? CollectionName,
    IReadOnlyDictionary<string, string> Information,
    IReadOnlyList<GCstarItem?> Items,
    IReadOnlyList<string> UnsupportedFields,
    int SkippedItemsCount,
    IReadOnlyList<string> SkippedReasons);
