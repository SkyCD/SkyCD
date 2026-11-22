using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;

/// <summary>
/// Structured document for a native abeMeda/NeoFinder import. Rows follow the
/// shared flat contract consumed by the catalog loader
/// (<c>id</c>/<c>name</c>/<c>parentId</c>/<c>type</c>/<c>size</c>/<c>properties</c>).
/// </summary>
public sealed class AbeMedaNativeReportDocument(
    IReadOnlyList<Dictionary<string, object?>> rows,
    AbeMedaNativeImportSummary summary) : IReadOnlyList<Dictionary<string, object?>>
{
    public AbeMedaNativeImportSummary Summary { get; } = summary;

    public IReadOnlyList<Dictionary<string, object?>> Rows { get; } = rows;

    public int Count => Rows.Count;

    public Dictionary<string, object?> this[int index] => Rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Rows.GetEnumerator();
}
