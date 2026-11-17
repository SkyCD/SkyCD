using System.Collections.Generic;

namespace SkyCD.Plugin.PlainText;

/// <summary>
/// Payload for the plain-text catalog-list export (format id <c>skycd-txt-list</c>).
/// Carries the visible result/list rows in export order plus the export configuration.
/// </summary>
public sealed record PlainTextListExportRequest
{
    /// <summary>
    /// Gets the rows to export in the exact order they must appear in the file.
    /// </summary>
    public required IEnumerable<Dictionary<string, object?>> Rows { get; init; }

    /// <summary>
    /// Gets the selected columns in deterministic export order. When null, the default
    /// column set (Name, Path, Kind, SizeBytes, Modified, Description, Disk) is used.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Gets the output layout. FixedWidth aligns columns; TabDelimited writes
    /// machine-friendly tab-separated values.
    /// </summary>
    public PlainTextListLayout Layout { get; init; } = PlainTextListLayout.FixedWidth;

    /// <summary>
    /// Gets the maximum column width in characters. Widths are the maximum of header
    /// and rendered values, capped here (default 120). Longer values are truncated
    /// with an ellipsis.
    /// </summary>
    public int MaxColumnWidth { get; init; } = 120;

    /// <summary>
    /// Gets explicit per-column widths (case-insensitive names) that override measured
    /// widths. Each value must be within the same bounds as <see cref="MaxColumnWidth"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int>? ColumnWidths { get; init; }

    /// <summary>
    /// Gets how many leading rows are measured when the row source cannot be
    /// re-enumerated. Bounds the memory of the width calculation pass.
    /// </summary>
    public int MaxWidthSampleRows { get; init; } = 20000;

    /// <summary>
    /// Gets the output encoding name: <c>utf-8</c> (with BOM, default),
    /// <c>utf-8-nobom</c>, or a Windows code page such as <c>windows-1252</c>.
    /// </summary>
    public string EncodingName { get; init; } = "utf-8";

    /// <summary>
    /// Gets whether to write the header row (and its separator row in fixed-width
    /// mode). Default <c>true</c>.
    /// </summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>
    /// Gets an optional column whose value groups rows. Groups stream in first-seen
    /// order with one blank line between groups. Null disables grouping.
    /// </summary>
    public string? GroupBy { get; init; }

    /// <summary>
    /// Gets an optional culture name (for example <c>de-DE</c>) used to format sizes
    /// and timestamps for display. Null (default) formats culture-invariantly.
    /// </summary>
    public string? CultureName { get; init; }
}

/// <summary>
/// Plain-text list layouts: human-readable aligned columns, or tab-delimited rows.
/// </summary>
public enum PlainTextListLayout
{
    FixedWidth,
    TabDelimited
}
