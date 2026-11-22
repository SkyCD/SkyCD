using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Structured document representing a DiskCatalogMaker v5 catalog import:
/// reconstructed catalog rows plus the import execution summary.
/// </summary>
public sealed class DiskCatalogMakerV5ReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public DiskCatalogMakerV5ReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        DiskCatalogMakerV5ImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public DiskCatalogMakerV5ImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
