using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.XmlExport.Models;

/// <summary>
/// Payload for the XML catalog and list exporters (format ids
/// <c>skycd-xml-catalog</c> and <c>skycd-xml-list</c>).
/// Carries the rows to export plus the document-level export configuration,
/// so the database name, optional stylesheet, and list column selection travel
/// with the data.
/// </summary>
public sealed record XmlExportRequest
{
    /// <summary>
    /// Gets the rows to export. Hierarchical exports link rows through
    /// <c>nodeId</c>/<c>parentId</c> keys and preserve input order at every
    /// level; flat-list exports emit rows in enumeration order.
    /// </summary>
    public required IEnumerable<Dictionary<string, object?>> Rows { get; init; }

    /// <summary>
    /// Gets the catalog name written to the root <c>Name</c> (hierarchical) or
    /// <c>Database</c> (flat list) attribute. Defaults to the output file name
    /// without extension, else <c>Catalog</c>.
    /// </summary>
    public string? DatabaseName { get; init; }

    /// <summary>
    /// Gets the optional XSL stylesheet reference. Must be a bare relative
    /// <c>.xsl</c> file name: no directories, no absolute paths, no traversal.
    /// </summary>
    public string? Stylesheet { get; init; }

    /// <summary>
    /// Gets the selected flat-list columns in exact export order. Resolved
    /// case-insensitively against row keys with canonical renames
    /// (<c>name</c> to <c>Name</c>, <c>size</c>/<c>sizeBytes</c> to
    /// <c>Size</c>, date keys to <c>Modified</c>). Flat-list profile only;
    /// when null, every non-structural row key exports in row order.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }
}
