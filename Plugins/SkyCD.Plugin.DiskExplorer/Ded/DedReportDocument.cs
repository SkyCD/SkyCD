using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Structured DED import result: catalog rows for the host plus the full parsed
/// database, which preserves opaque records for lossless round trips.
/// </summary>
public sealed class DedReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public DedReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        DedDatabase database,
        DedImportSummary summary)
    {
        this.rows = rows;
        Database = database;
        Summary = summary;
    }

    public DedDatabase Database { get; }

    public DedImportSummary Summary { get; }

    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
