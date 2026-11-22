using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Models;

/// <summary>
/// Structured document representing a NeoFinder text export import, containing
/// both reconstructed catalog nodes and the execution summary.
/// </summary>
public sealed class NeoFinderTextReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public NeoFinderTextReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        NeoFinderTextImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public NeoFinderTextImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
