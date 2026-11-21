using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Native.Models;

/// <summary>
/// Summary details for a native WinCatalog catalog import execution.
/// </summary>
public sealed record WinCatalogNativeImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    IReadOnlyList<string> DetectedTables,
    string DetectedSchema);
