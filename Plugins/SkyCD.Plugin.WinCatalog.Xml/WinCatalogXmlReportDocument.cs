using System.Collections;
using System.Collections.Generic;
using SkyCD.Plugin.WinCatalog.Csv;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Structured document representing a WinCatalog XML report import, containing both
/// reconstructed catalog nodes and the execution summary. Node rows use the same
/// payload contract as the WinCatalog CSV importer.
/// </summary>
public sealed class WinCatalogXmlReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public WinCatalogXmlReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        WinCatalogXmlImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    public static WinCatalogXmlReportDocument FromCsv(WinCatalogCsvReportDocument csvDocument, WinCatalogXmlImportSummary summary)
    {
        return new WinCatalogXmlReportDocument(csvDocument, summary);
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public WinCatalogXmlImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
