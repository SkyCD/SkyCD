using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Access.Models;

public sealed record AccessListColumn(string Key, string DisplayName);

public sealed record AccessListExportRequest
{
    public required IEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; init; }

    public IReadOnlyList<AccessListColumn> Columns { get; init; } =
    [new("Name", "Name"), new("Path", "Path"), new("Kind", "Kind"), new("SizeBytes", "SizeBytes"),
        new("Modified", "Modified"), new("Description", "Description"), new("Disk", "Disk")];
}
