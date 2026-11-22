using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Parsing;

/// <summary>
/// Entries parsed from the documented record table plus any salvage diagnostics.
/// </summary>
public sealed record AbeMedaNativeEntryScan(
    IReadOnlyList<AbeMedaNativeEntry> Entries,
    IReadOnlyList<string> Warnings,
    int SkippedCount);
