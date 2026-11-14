using System.Collections.Generic;

namespace SkyCD.Plugin.Cathy;

/// <summary>
/// Summary statistics for Cathy catalog import.
/// </summary>
public sealed record CathyImportSummary(
    short Version,
    string VolumeName,
    int TotalFolders,
    int TotalFiles,
    long TotalSizeBytes,
    uint FreeSizeMb,
    bool IsArchive,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> UnsupportedFeatures);
