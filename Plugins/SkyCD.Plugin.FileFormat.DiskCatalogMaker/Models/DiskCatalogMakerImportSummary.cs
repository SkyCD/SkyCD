using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Summary details for a DiskCatalogMaker text-export import execution.
/// </summary>
public sealed record DiskCatalogMakerImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    char DetectedDelimiter,
    IReadOnlyList<string> DetectedColumns,
    string DetectedEncoding);
