using System.Collections.Generic;

namespace SkyCD.Plugin.Vvv.Models;

/// <summary>
/// VVV export scope. Physical exports carry volume + path columns and
/// reconstruct the cataloged hierarchy; virtual-folder exports carry only a
/// physical-origin path per file, so the virtual folder structure itself is
/// not representable.
/// </summary>
public enum VvvExportScope
{
    Physical,
    Virtual
}

/// <summary>
/// One parsed VVV file entry. For physical exports <see cref="PhysicalPath"/>
/// is null; for virtual exports <see cref="Volume"/> is empty and
/// <see cref="PhysicalPath"/> carries the full origin path.
/// </summary>
public sealed record VvvFileEntry(
    string Volume,
    string Path,
    string Name,
    long Size,
    string Extension,
    string LastModified,
    string Description,
    string? PhysicalPath);

/// <summary>
/// Result of parsing a VVV export.
/// </summary>
public sealed record VvvParseResult(
    VvvExportScope Scope,
    char Delimiter,
    bool HasHeader,
    IReadOnlyList<VvvFileEntry> Entries,
    int SkippedCount,
    IReadOnlyList<string> SkippedReasons,
    IReadOnlyList<string> Warnings);
