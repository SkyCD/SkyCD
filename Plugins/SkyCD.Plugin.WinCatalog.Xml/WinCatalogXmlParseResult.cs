using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Result of streaming-parsing a WinCatalog XML report into entry items plus report metadata.
/// </summary>
/// <param name="RootElement">Local name of the XML root element.</param>
/// <param name="SchemaVersion">Detected report schema version attribute, when present.</param>
/// <param name="Items">Sparse list of parsed items indexed by their provisional index.</param>
/// <param name="UnsupportedFields">Field names that were present but not mapped.</param>
/// <param name="SkippedItemsCount">Items skipped because they carried no recognized fields.</param>
/// <param name="SkippedReasons">Diagnostics for skipped items.</param>
public sealed record WinCatalogXmlParseResult(
    string? RootElement,
    string? SchemaVersion,
    IReadOnlyList<WinCatalogXmlItem?> Items,
    IReadOnlyList<string> UnsupportedFields,
    int SkippedItemsCount,
    IReadOnlyList<string> SkippedReasons);
