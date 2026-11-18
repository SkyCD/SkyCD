using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.WinCatalog.Native;

/// <summary>
/// Structured document representing a native WinCatalog catalog import.
/// Node rows use the shared flat contract
/// (<c>nodeId</c>/<c>parentId</c>/<c>kind</c>/<c>name</c>/<c>sizeBytes</c>/<c>properties</c>).
/// </summary>
public sealed class WinCatalogNativeReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public WinCatalogNativeReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        WinCatalogNativeImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    public WinCatalogNativeImportSummary Summary { get; }

    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
