using System.Collections.Generic;

namespace SkyCD.Plugin.WhereIsIt.Models;

/// <summary>
/// One parsed report entry. Indexes follow document (start-element) order.
/// </summary>
public sealed record WhereIsItItem(
    int Index,
    WhereIsItElementKind Kind,
    string? RawItemType,
    int ParentIndex,
    Dictionary<WhereIsItField, string> Fields,
    Dictionary<string, string> ExtraFields);

/// <summary>
/// Result of parsing a Where Is It? XML report.
/// </summary>
public sealed record WhereIsItParseResult(
    string? Title,
    IReadOnlyList<WhereIsItItem?> Items,
    IReadOnlyList<string> UnsupportedFields,
    int SkippedItemsCount,
    IReadOnlyList<string> SkippedReasons);
