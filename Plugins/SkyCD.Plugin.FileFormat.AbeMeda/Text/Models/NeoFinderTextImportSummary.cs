using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Models;

/// <summary>
/// Summary details for a NeoFinder text export import execution.
/// </summary>
public sealed record NeoFinderTextImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    char DetectedDelimiter,
    IReadOnlyList<string> DetectedColumns,
    string DetectedEncoding);
