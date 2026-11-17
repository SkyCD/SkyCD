using System.Collections.Generic;

namespace SkyCD.Plugin.AbeMeda;

public sealed class AbeMedaImportSummary
{
    public int Volumes { get; set; }

    public int Folders { get; set; }

    public int Files { get; set; }

    public int SkippedRows { get; set; }

    public string? SchemaVersion { get; set; }

    public string ImportMode { get; set; } = "Nested";

    public List<string> Notes { get; } = [];
}
