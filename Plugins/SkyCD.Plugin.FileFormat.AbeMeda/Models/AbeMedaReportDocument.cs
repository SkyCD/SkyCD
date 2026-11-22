using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Models;

/// <summary>
/// Structured document of an abeMeda XML import: reconstructed catalog rows
/// consumed by the normal catalog loader, plus the import execution summary.
/// </summary>
public sealed class AbeMedaReportDocument(
    IReadOnlyList<Dictionary<string, object?>> rows,
    AbeMedaImportSummary summary) : IReadOnlyList<Dictionary<string, object?>>
{
    public AbeMedaImportSummary Summary { get; } = summary;

    public IReadOnlyList<Dictionary<string, object?>> Rows { get; } = rows;

    public int Count => Rows.Count;

    public Dictionary<string, object?> this[int index] => Rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Rows.GetEnumerator();
}
