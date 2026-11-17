using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer;

/// <summary>
/// Payload for the Disk Explorer XML report export (format id
/// <c>disk-explorer-xml-report</c>). Rows stream in encounter order; parents link
/// by id so the recursive report hierarchy rebuilds deterministically.
/// </summary>
public sealed record DiskExplorerExportRequest
{
    public required IEnumerable<Dictionary<string, object?>> Rows { get; init; }

    public string? Title { get; init; }
}
