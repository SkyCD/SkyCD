using System.Collections.Generic;

namespace SkyCD.Plugin.CdPlayer.Ini.Models;

/// <summary>
/// Summary of CDPlayer.ini import diagnostics and metrics.
/// </summary>
public sealed record CdPlayerIniImportSummary(
    int TotalDiscsRead,
    int TotalTracksRead,
    IReadOnlyList<string> Discs,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics,
    string DetectedEncoding);
