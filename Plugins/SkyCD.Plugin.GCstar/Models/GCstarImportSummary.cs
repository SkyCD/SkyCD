using System.Collections.Generic;

namespace SkyCD.Plugin.GCstar.Models;

public sealed class GCstarImportSummary
{
    public int Volumes { get; set; }

    public int Files { get; set; }

    public int SkippedRows { get; set; }

    public string? CollectionType { get; set; }

    public string? Version { get; set; }

    public string? CollectionName { get; set; }

    public List<string> Notes { get; } = [];
}
