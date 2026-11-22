using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Summary of one v5 catalog import, including non-fatal diagnostics.
/// </summary>
public sealed record DiskCatalogMakerV5ImportSummary(
    uint CatalogRevision,
    int TableCount,
    int KnownTableCount,
    int UnknownTableCount,
    int VolumeCount,
    int ItemCount,
    int ImportedItemCount,
    string? PackageRoot,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DiskCatalogMakerV5UnknownTable> UnknownTables);
