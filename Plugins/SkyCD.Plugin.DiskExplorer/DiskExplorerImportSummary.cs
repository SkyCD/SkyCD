using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer;

public sealed class DiskExplorerImportSummary
{
    public int Volumes { get; set; }

    public int Folders { get; set; }

    public int Files { get; set; }

    public int SkippedRows { get; set; }

    public string? Title { get; set; }

    public List<string> Notes { get; } = [];
}
