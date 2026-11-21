using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.WhereIsIt.Models;

/// <summary>
/// Structured document of a Where Is It? import: reconstructed catalog rows
/// consumed by the normal catalog loader, plus the import execution summary.
/// </summary>
public sealed class WhereIsItReportDocument(
    IReadOnlyList<Dictionary<string, object?>> rows,
    WhereIsItImportSummary summary) : IReadOnlyList<Dictionary<string, object?>>
{
    public WhereIsItImportSummary Summary { get; } = summary;

    public IReadOnlyList<Dictionary<string, object?>> Rows { get; } = rows;

    public int Count => Rows.Count;

    public Dictionary<string, object?> this[int index] => Rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Rows.GetEnumerator();
}
