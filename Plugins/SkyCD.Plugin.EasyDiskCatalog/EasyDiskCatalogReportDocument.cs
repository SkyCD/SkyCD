using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.EasyDiskCatalog;

/// <summary>
/// Structured document of an Easy Disk Catalog import: reconstructed catalog rows
/// consumed by the normal catalog loader, plus the import execution summary.
/// </summary>
public sealed class EasyDiskCatalogReportDocument(
    IReadOnlyList<Dictionary<string, object?>> rows,
    EasyDiskCatalogImportSummary summary) : IReadOnlyList<Dictionary<string, object?>>
{
    public EasyDiskCatalogImportSummary Summary { get; } = summary;

    public IReadOnlyList<Dictionary<string, object?>> Rows { get; } = rows;

    public int Count => Rows.Count;

    public Dictionary<string, object?> this[int index] => Rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => Rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Rows.GetEnumerator();
}
