using System.Collections.Generic;
using SkyCD.Plugin.AbeMeda.Native.Parsing;

namespace SkyCD.Plugin.AbeMeda.Native.Models;

/// <summary>
/// Result of reading one native catalog container.
/// </summary>
public sealed record AbeMedaNativeCatalog(
    AbeMedaNativeGeneration Generation,
    AbeMedaNativeHeader Header,
    long FileLength,
    IReadOnlyList<AbeMedaNativeEntry> Entries,
    IReadOnlyList<string> Warnings,
    int SkippedEntryCount);
