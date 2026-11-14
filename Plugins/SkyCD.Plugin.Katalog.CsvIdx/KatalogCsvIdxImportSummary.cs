using System.Collections.Generic;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// Summary report of the Katalog Memory-mode CSV/IDX collection folder import.
/// </summary>
public sealed record KatalogCsvIdxImportSummary(
    string AppVersion,
    int TotalDevices,
    int TotalCatalogs,
    int TotalFolders,
    int TotalFiles,
    long TotalSizeBytes,
    int BrokenReferencesCount,
    int CorruptedRecordsCount,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> UnsupportedFeatures);
