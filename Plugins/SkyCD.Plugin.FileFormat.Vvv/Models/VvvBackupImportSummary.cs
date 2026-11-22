using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Vvv.Models;

/// <summary>
/// Execution details for a VVV logical-backup import: verified schema
/// version, mapped node counts and skipped-row notes.
/// </summary>
public sealed class VvvBackupImportSummary
{
    public int SchemaVersion { get; init; }

    public string? SourceName { get; init; }

    public int Volumes { get; init; }

    public int Folders { get; init; }

    public int Files { get; init; }

    public int VirtualFolders { get; init; }

    public int VirtualFiles { get; init; }

    public int SkippedRows { get; init; }

    public List<string> Notes { get; } = [];
}
