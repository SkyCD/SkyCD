using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Vvv.Models;

public sealed class VvvImportSummary
{
    public VvvExportScope Scope { get; set; }

    public char Delimiter { get; set; }

    public bool HasHeader { get; set; }

    public int Volumes { get; set; }

    public int Folders { get; set; }

    public int Files { get; set; }

    public int SkippedRows { get; set; }

    public List<string> Notes { get; } = [];
}
