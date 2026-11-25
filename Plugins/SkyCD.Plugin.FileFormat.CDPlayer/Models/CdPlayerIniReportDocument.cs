using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.CDPlayer.Models;

/// <summary>
/// Structured document representing a CDPlayer.ini catalog import, implementing
/// IReadOnlyList&lt;Dictionary&lt;string, object?&gt;&gt; for compatibility with SkyCD catalog import pipeline.
/// </summary>
public sealed class CdPlayerIniReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public CdPlayerIniReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        CdPlayerIniImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public CdPlayerIniImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
