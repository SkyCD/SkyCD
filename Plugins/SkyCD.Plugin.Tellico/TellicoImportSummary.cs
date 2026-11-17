using System.Collections.Generic;

namespace SkyCD.Plugin.Tellico;

public sealed class TellicoImportSummary
{
    public int Collections { get; set; }

    public int Entries { get; set; }

    public int SkippedEntries { get; set; }

    public string? SyntaxVersion { get; set; }

    public List<string> Notes { get; } = [];
}
