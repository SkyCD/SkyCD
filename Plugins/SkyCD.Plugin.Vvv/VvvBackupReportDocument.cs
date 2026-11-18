using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.Vvv;

/// <summary>
/// Structured document of a VVV logical-backup import: catalog rows consumed
/// by the normal catalog loader plus the import execution summary.
/// </summary>
public sealed class VvvBackupReportDocument(
    IReadOnlyList<Dictionary<string, object?>> rows,
    VvvBackupImportSummary summary) : IReadOnlyList<Dictionary<string, object?>>
{
    public VvvBackupImportSummary Summary { get; } = summary;

    public IReadOnlyList<Dictionary<string, object?>> Rows { get; } = rows;

    public int Count => Rows.Count;

    public Dictionary<string, object?> this[int index] => Rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
