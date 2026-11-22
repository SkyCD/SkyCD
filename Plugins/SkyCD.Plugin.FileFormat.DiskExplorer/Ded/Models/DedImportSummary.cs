using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;

/// <summary>Counts and notes produced while reading a DED database.</summary>
public sealed class DedImportSummary
{
    public string Title { get; set; } = string.Empty;

    public int Objects { get; set; }

    public int Volumes { get; set; }

    public int Categories { get; set; }

    public int Folders { get; set; }

    public int Archives { get; set; }

    public int Files { get; set; }

    public int UnknownObjects { get; set; }

    public List<string> Notes { get; } = [];
}
