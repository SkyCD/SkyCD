using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer.Html.Models;

/// <summary>
/// Payload for the Disk Explorer HTML report export (format id
/// <c>disk-explorer-html-report</c>). The bundle (root page plus sibling directory)
/// is written next to the file target, so export requires a file stream.
/// </summary>
public sealed record DiskExplorerHtmlExportRequest
{
    public required IEnumerable<Dictionary<string, object?>> Rows { get; init; }

    public string? Title { get; init; }

    /// <summary>
    /// Output encoding name: <c>utf-8</c> (default) or <c>windows-1252</c> for compatibility.
    /// </summary>
    public string EncodingName { get; init; } = "utf-8";

    /// <summary>
    /// Copies row <c>ThumbnailFile</c> assets into the bundle when true. Default true.
    /// </summary>
    public bool IncludeThumbnails { get; init; } = true;
}
