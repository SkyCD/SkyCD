using System.Collections.Generic;

namespace SkyCD.Plugin.CdCollect.Models;

/// <summary>
/// Execution summary of importing a CDcollect catalog database.
/// </summary>
public sealed record CdCollectImportSummary(
    int CatalogVersion,
    int MediaCount,
    int DirectoryCount,
    int FileCount,
    long TotalSizeBytes,
    int InfoPluginsCount,
    int CompPluginsCount,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> UnsupportedFeatures);
