using System;

namespace SkyCD.Plugin.Librer;

/// <summary>
/// Summary statistics and metadata from a Librer catalog import.
/// </summary>
public sealed class LibrerImportSummary
{
    public string CatalogLabel { get; set; } = string.Empty;
    public string ScanPath { get; set; } = string.Empty;
    public string DataFormatVersion { get; set; } = string.Empty;
    public DateTimeOffset? CreationTime { get; set; }
    public string CreationHost { get; set; } = string.Empty;
    public string CreationOS { get; set; } = string.Empty;
    public long TotalFiles { get; set; }
    public long TotalFolders { get; set; }
    public long TotalSizeBytes { get; set; }
    public long FreeSizeBytes { get; set; } = -1;
    public int CustomDataCount { get; set; }
    public string Comments { get; set; } = string.Empty;
}
