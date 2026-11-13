using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Summary details for a WinCatalog XML import execution.
/// </summary>
public sealed record WinCatalogXmlImportSummary(
    int TotalItemsRead,
    int ImportedItemsCount,
    int SkippedItemsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    string? DetectedRootElement,
    string? DetectedSchemaVersion,
    string ImportMode);
