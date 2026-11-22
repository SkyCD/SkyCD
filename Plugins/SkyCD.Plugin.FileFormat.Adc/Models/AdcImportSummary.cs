using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Adc.Models;

/// <summary>
/// Summary details for one Advanced Disk Catalog import execution.
/// </summary>
public sealed record AdcImportSummary(
    int TotalRecordsRead,
    int ImportedRecordsCount,
    int SkippedRecordsCount,
    bool IsPartialReport,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> UnsupportedFields,
    string DetectedVersion,
    int VolumeCount,
    int ItemCount,
    int CategoryCount,
    int CommentCount);
