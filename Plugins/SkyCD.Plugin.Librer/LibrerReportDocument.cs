using System.Collections;
using System.Collections.Generic;

namespace SkyCD.Plugin.Librer;

/// <summary>
/// A document containing the parsed catalog rows and summary from a Librer file.
/// </summary>
public sealed class LibrerReportDocument : IReadOnlyList<Dictionary<string, object?>>
{
    private readonly List<Dictionary<string, object?>> _rows;

    public LibrerImportSummary Summary { get; }

    public IReadOnlyList<Dictionary<string, object?>> Rows => _rows;

    public LibrerReportDocument(List<Dictionary<string, object?>> rows, LibrerImportSummary summary)
    {
        _rows = rows;
        Summary = summary;
    }

    public int Count => _rows.Count;

    public Dictionary<string, object?> this[int index] => _rows[index];

    public IEnumerator<Dictionary<string, object?>> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
