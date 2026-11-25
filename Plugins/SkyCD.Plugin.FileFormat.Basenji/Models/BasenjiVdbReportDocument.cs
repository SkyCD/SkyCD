using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Basenji.Models;

/// <summary>
/// Structured document representing a Basenji .vdb catalog import, containing
/// both reconstructed catalog nodes and the execution summary.
/// </summary>
public sealed class BasenjiVdbReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public BasenjiVdbReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        BasenjiVdbImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public BasenjiVdbImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
