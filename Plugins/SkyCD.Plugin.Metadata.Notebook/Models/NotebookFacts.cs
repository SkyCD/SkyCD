using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.Notebook.Models;

/// <summary>Facts collected from one notebook, plus the position of a parse failure.</summary>
internal sealed record NotebookFacts
{
    public required bool Valid { get; init; }

    public required long ByteLength { get; init; }

    public string? ErrorCode { get; init; }

    public int? ErrorLine { get; init; }

    public int? ErrorColumn { get; init; }

    public string? NbFormatVersion { get; init; }

    public string? Language { get; init; }

    public int? CellCount { get; init; }

    public int? IncludedCellCount { get; init; }

    public IReadOnlyList<string> CellTitles { get; init; } = [];
}
