using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.BrokenX.Models;

/// <summary>
/// Summary details for a Broken X database import execution.
/// </summary>
public sealed record BxdImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    string DetectedVersion);
