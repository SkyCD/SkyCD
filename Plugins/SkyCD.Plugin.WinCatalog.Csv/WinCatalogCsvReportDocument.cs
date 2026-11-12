using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Structured document representing a WinCatalog CSV report import, containing
/// both reconstructed catalog nodes and the execution summary.
/// </summary>
public sealed class WinCatalogCsvReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public WinCatalogCsvReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        WinCatalogCsvImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public WinCatalogCsvImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
