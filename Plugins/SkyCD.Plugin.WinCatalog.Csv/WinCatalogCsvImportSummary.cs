using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Summary details for a WinCatalog CSV import execution.
/// </summary>
public sealed record WinCatalogCsvImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    char DetectedDelimiter,
    IReadOnlyList<string> DetectedColumns);
