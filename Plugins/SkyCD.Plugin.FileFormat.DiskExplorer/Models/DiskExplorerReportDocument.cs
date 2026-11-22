using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Models;

/// <summary>
/// Structured document of a Disk Explorer import: reconstructed catalog rows
/// consumed by the normal catalog loader, plus the import execution summary.
/// </summary>
public sealed class DiskExplorerReportDocument(
    IReadOnlyList<Dictionary<string, object?>> rows,
    DiskExplorerImportSummary summary) : IReadOnlyList<Dictionary<string, object?>>
{
    public DiskExplorerImportSummary Summary { get; } = summary;

    public IReadOnlyList<Dictionary<string, object?>> Rows { get; } = rows;

    public int Count => Rows.Count;

    public Dictionary<string, object?> this[int index] => Rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Rows.GetEnumerator();
}
