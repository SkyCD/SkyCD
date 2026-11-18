using System.Collections.Generic;

namespace SkyCD.Plugin.AbeMeda.Native;

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
