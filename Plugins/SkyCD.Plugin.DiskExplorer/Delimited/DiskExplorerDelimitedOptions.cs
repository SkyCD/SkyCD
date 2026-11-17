using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer.Delimited;

/// <summary>
/// Export options for the Disk Explorer delimited-text profile
/// (format id <c>disk-explorer-delimited</c>). The dialect is explicit here;
/// import auto-detects only unambiguous dialects.
/// </summary>
public sealed record DiskExplorerDelimitedExportRequest
{
    public required IEnumerable<Dictionary<string, object?>> Rows { get; init; }

    public string? Title { get; init; }

    /// <summary>
    /// Ordered column labels to write. Defaults to
    /// VOLUME, PATH, ITEMTYPE, NAME, SIZE, DATE, TIME, TYPE, DESC.
    /// Unknown labels are written as empty fields.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Field separator. Defaults to comma.
    /// </summary>
    public char Separator { get; init; } = ',';

    /// <summary>
    /// Output encoding. Defaults to UTF-8 with BOM; Windows-1252 selects
    /// compatibility mode (CRLF line endings).
    /// </summary>
    public DiskExplorerDelimitedEncoding Encoding { get; init; } = DiskExplorerDelimitedEncoding.Utf8Bom;

    /// <summary>
    /// Line terminator override. Defaults to LF, or CRLF in compatibility mode.
    /// </summary>
    public string? NewLine { get; init; }

    /// <summary>
    /// Prefix fields starting with = + - @ with a single quote to prevent
    /// spreadsheet formula injection. Defaults to true.
    /// </summary>
    public bool GuardFormulas { get; init; } = true;

    /// <summary>
    /// Skip formula guarding and emit fields verbatim. Documented for
    /// byte-exact compatibility comparisons only.
    /// </summary>
    public bool RawCompatibility { get; init; }
}

/// <summary>
/// Output text encodings for the delimited profile.
/// </summary>
public enum DiskExplorerDelimitedEncoding
{
    Utf8Bom,
    Windows1252
}

/// <summary>
/// Column labels of the delimited profile. Structural labels drive hierarchy
/// and identity; field labels map to catalog fields; anything else is
/// retained as extension metadata on import and written empty on export.
/// </summary>
public static class DiskExplorerDelimitedColumns
{
    public static readonly IReadOnlyList<string> DefaultExportColumns =
    [
        "VOLUME", "PATH", "ITEMTYPE", "NAME", "SIZE", "DATE", "TIME", "TYPE", "DESC"
    ];

    public static readonly IReadOnlyList<string> AllKnownColumns =
    [
        "VOLUME", "CATEGORY", "PATH", "FULLPATH", "ITEMTYPE", "KIND",
        "NAME", "FILES", "FOLDERS", "SIZE", "DATE", "TIME", "TYPE", "DESC"
    ];

    public static bool IsStructural(string label) =>
        label.Equals("VOLUME", StringComparison.OrdinalIgnoreCase)
        || label.Equals("CATEGORY", StringComparison.OrdinalIgnoreCase)
        || label.Equals("PATH", StringComparison.OrdinalIgnoreCase)
        || label.Equals("FULLPATH", StringComparison.OrdinalIgnoreCase)
        || label.Equals("ITEMTYPE", StringComparison.OrdinalIgnoreCase)
        || label.Equals("KIND", StringComparison.OrdinalIgnoreCase);
}
