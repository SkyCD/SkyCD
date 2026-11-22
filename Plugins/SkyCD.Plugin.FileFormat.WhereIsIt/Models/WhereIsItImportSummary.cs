using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.WhereIsIt.Models;

public sealed class WhereIsItImportSummary
{
    public int Volumes { get; set; }

    public int Folders { get; set; }

    public int Files { get; set; }

    public long TotalBytes { get; set; }

    public int SkippedRows { get; set; }

    public string? Title { get; set; }

    public List<string> Notes { get; } = [];
}
