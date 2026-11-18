using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.SmartCdCatalogPro;

/// <summary>
/// Structured document representing a Smart CD Catalog PRO import. Node rows use the
/// shared flat contract
/// (<c>nodeId</c>/<c>parentId</c>/<c>kind</c>/<c>name</c>/<c>sizeBytes</c>/<c>properties</c>).
/// </summary>
public sealed class SmartCdCatalogProReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public SmartCdCatalogProReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        SmartCdCatalogProImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    public SmartCdCatalogProImportSummary Summary { get; }

    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
