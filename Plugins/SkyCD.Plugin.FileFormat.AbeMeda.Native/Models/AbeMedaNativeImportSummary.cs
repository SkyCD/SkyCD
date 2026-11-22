using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;

/// <summary>
/// Execution details for a native abeMeda/NeoFinder import. Item counts are
/// intentionally absent: only the verified container and artifact-record layer
/// is decoded until genuine fixtures validate the item tables.
/// </summary>
public sealed class AbeMedaNativeImportSummary
{
    public string Generation { get; init; } = "Classic";

    public uint Version { get; init; }

    public uint CompatibilityVersion { get; init; }

    public long FileLength { get; init; }

    public uint HeaderSize { get; init; }

    public uint SectionOffset { get; init; }

    public uint SectionLength { get; init; }

    public uint BlockSize { get; init; }

    public uint DeclaredEntryCount { get; init; }

    public int ImportedEntryCount { get; init; }

    public int SkippedEntryCount { get; init; }

    public int WarningCount => Warnings.Count;

    public Dictionary<string, int> HandlerCounts { get; } = new(System.StringComparer.Ordinal);

    public List<string> Warnings { get; } = [];

    public List<string> Notes { get; } = [];
}
