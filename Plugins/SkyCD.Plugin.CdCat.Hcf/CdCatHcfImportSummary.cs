using System.Collections.Generic;

namespace SkyCD.Plugin.CdCat.Hcf;

/// <summary>
/// Summary details for a CdCat HCF import execution.
/// </summary>
public sealed record CdCatHcfImportSummary(
    string CatalogVersion,
    int MediaCount,
    int DirectoryCount,
    int FileCount,
    long TotalSizeBytes,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> UnsupportedFeatures);
