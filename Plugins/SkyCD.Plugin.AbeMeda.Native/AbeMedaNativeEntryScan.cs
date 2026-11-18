using System.Collections.Generic;

namespace SkyCD.Plugin.AbeMeda.Native;

/// <summary>
/// Entries parsed from the documented record table plus any salvage diagnostics.
/// </summary>
public sealed record AbeMedaNativeEntryScan(
    IReadOnlyList<AbeMedaNativeEntry> Entries,
    IReadOnlyList<string> Warnings,
    int SkippedCount);
