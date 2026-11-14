using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.CdCat.Hcf;

/// <summary>
/// Structured document representing a CdCat HCF catalog import, containing
/// both reconstructed catalog nodes and the execution summary.
/// </summary>
public sealed class CdCatHcfReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly IReadOnlyList<Dictionary<string, object?>> rows;

    public CdCatHcfReportDocument(
        IReadOnlyList<Dictionary<string, object?>> rows,
        CdCatHcfImportSummary summary)
    {
        this.rows = rows;
        Summary = summary;
    }

    /// <summary>
    /// Gets the import execution summary.
    /// </summary>
    public CdCatHcfImportSummary Summary { get; }

    /// <summary>
    /// Gets the reconstructed node dictionaries.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows => rows;

    public int Count => rows.Count;

    public Dictionary<string, object?> this[int index] => rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rows.GetEnumerator();
}
