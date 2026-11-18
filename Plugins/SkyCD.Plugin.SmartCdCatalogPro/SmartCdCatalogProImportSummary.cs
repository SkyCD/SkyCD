using System.Collections.Generic;

namespace SkyCD.Plugin.SmartCdCatalogPro;

/// <summary>Counts and non-fatal observations for one catalog import.</summary>
public sealed record SmartCdCatalogProImportSummary(
    int MediaRootCount,
    int FolderCount,
    int FileCount,
    int CategoryCount,
    int BoxCount,
    int BorrowerCount,
    int LoanHistoryCount,
    IReadOnlyList<string> Diagnostics);
