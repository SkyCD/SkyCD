namespace SkyCD.Plugin.FileFormat.Basenji.Models;

/// <summary>
/// Execution summary of a Basenji .vdb catalog import operation.
/// </summary>
public sealed record BasenjiVdbImportSummary
{
    public string DatabaseName { get; init; } = string.Empty;

    public string DatabaseDescription { get; init; } = string.Empty;

    public int DatabaseVersion { get; init; }

    public string DatabaseGuid { get; init; } = string.Empty;

    public int MediaCount { get; init; }

    public int DirectoryCount { get; init; }

    public int FileCount { get; init; }

    public long TotalSizeBytes { get; init; }
}
