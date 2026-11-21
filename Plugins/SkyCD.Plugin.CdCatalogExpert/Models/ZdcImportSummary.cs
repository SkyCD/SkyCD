using System.Collections.Generic;

namespace SkyCD.Plugin.CdCatalogExpert.Models;

/// <summary>
/// Summary details for a CD Catalog Expert import execution.
/// </summary>
public sealed record ZdcImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    string DetectedVersion);
