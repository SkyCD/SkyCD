using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Csv.Models;

/// <summary>
/// Payload for the configurable catalog-list CSV export (format id <c>skycd-csv-list</c>).
/// Carries the visible result/list rows in export order plus the export configuration,
/// so column selection, order, delimiter, encoding and header settings travel with the data.
/// </summary>
public sealed record CsvListExportRequest
{
    /// <summary>
    /// Gets the rows to export in the exact order they must appear in the file.
    /// Rows stream through the writer; the full set is never buffered.
    /// </summary>
    public required IEnumerable<Dictionary<string, object?>> Rows { get; init; }

    /// <summary>
    /// Gets the selected columns in deterministic export order. When null, the default
    /// column set (Name, Path, Kind, SizeBytes, Modified, Description, Disk) is used.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Gets the one-character field separator. Comma by default; semicolon and tab supported.
    /// </summary>
    public char Separator { get; init; } = ',';

    /// <summary>
    /// Gets the output encoding name: <c>utf-8</c> (with BOM, default),
    /// <c>utf-8-nobom</c>, or a Windows code page such as <c>windows-1252</c>.
    /// </summary>
    public string EncodingName { get; init; } = "utf-8";

    /// <summary>
    /// Gets whether to write the header row. Default <c>true</c>.
    /// </summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>
    /// Gets whether spreadsheet-formula-looking values are written exactly as stored.
    /// Default <c>false</c>: values beginning with <c>=</c>, <c>+</c>, <c>-</c> or
    /// <c>@</c> are prefixed with <c>'</c> so spreadsheets treat them as text.
    /// </summary>
    public bool AllowFormulas { get; init; } = false;
}
