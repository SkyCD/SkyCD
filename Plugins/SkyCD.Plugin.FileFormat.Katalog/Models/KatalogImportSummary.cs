using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Katalog.Models;

/// <summary>
/// Summary report of the Katalog Memory-mode CSV/IDX collection folder import.
/// </summary>
public sealed record KatalogImportSummary(
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
