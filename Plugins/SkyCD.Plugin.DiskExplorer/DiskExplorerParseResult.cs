using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer;

/// <summary>
/// One parsed report entry. Indexes follow document (start-element) order.
/// </summary>
public sealed record DiskExplorerItem(
    int Index,
    DiskExplorerElementKind Kind,
    string? RawItemType,
    int ParentIndex,
    bool HasItemChildren,
    bool HasThumbnail,
    Dictionary<DiskExplorerField, string> Fields,
    Dictionary<string, string> ExtraFields);

/// <summary>
/// Result of parsing a Disk Explorer Professional XML report.
/// </summary>
public sealed record DiskExplorerParseResult(
    string? Title,
    IReadOnlyList<DiskExplorerItem?> Items,
    IReadOnlyList<string> UnsupportedFields,
    int SkippedItemsCount,
    IReadOnlyList<string> SkippedReasons);
