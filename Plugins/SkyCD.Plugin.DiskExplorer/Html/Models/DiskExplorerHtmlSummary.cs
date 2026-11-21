using System.Collections.Generic;

namespace SkyCD.Plugin.DiskExplorer.Html.Models;

public sealed class DiskExplorerHtmlImportSummary
{
    public int Volumes { get; set; }

    public int Folders { get; set; }

    public int Files { get; set; }

    public int PagesVisited { get; set; }

    public int MissingPages { get; set; }

    public int MissingAssets { get; set; }

    public int CyclesDetected { get; set; }

    public int SkippedExternalLinks { get; set; }

    public int SkippedUnsafeLinks { get; set; }

    public int SkippedRows { get; set; }

    public bool Partial { get; set; }

    public string? Title { get; set; }

    public string? EncodingName { get; set; }

    public List<string> Notes { get; } = [];
}
