using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Models;

/// <summary>
/// Parsed <c>zdc_index.xml</c> catalog: ordered groups and discs.
/// </summary>
/// <remarks>
/// Numeric attribute values are kept as their original invariant decimal text
/// so saving never loses precision or rewrites a legacy representation.
/// Unknown attributes are retained in <see cref="ExtensionAttributes"/>.
/// </remarks>
public sealed class ZdcCatalog
{
    /// <summary>Catalog name (<c>Z.N</c>); required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Catalog description (<c>Z.D</c>); default empty.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Unknown attributes retained for round trip.</summary>
    public List<KeyValuePair<string, string>> ExtensionAttributes { get; } = [];

    /// <summary>Ordered groups and discs (<c>G</c>/<c>L</c> children).</summary>
    public List<ZdcCatalogEntry> Children { get; } = [];
}

/// <summary>Base type for an ordered index entry.</summary>
public abstract class ZdcCatalogEntry
{
    /// <summary>Unknown attributes retained for round trip.</summary>
    public List<KeyValuePair<string, string>> ExtensionAttributes { get; } = [];
}

/// <summary>Category group (<c>G</c>); nests groups and discs.</summary>
public sealed class ZdcGroup : ZdcCatalogEntry
{
    /// <summary>Category name (<c>G.N</c>); required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Raw legacy timestamp (<c>G.C</c>); original text.</summary>
    public string? Timestamp { get; set; }

    /// <summary>Ordered nested groups and discs.</summary>
    public List<ZdcCatalogEntry> Children { get; } = [];
}

/// <summary>Disc/media record (<c>L</c>).</summary>
public sealed class ZdcDisc : ZdcCatalogEntry
{
    /// <summary>Display name (<c>L.N</c>); required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Disc-tree GUID text (<c>L.F</c>) exactly as stored.</summary>
    public string GuidText { get; set; } = string.Empty;

    /// <summary>Original source/root path (<c>L.P</c>).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Raw created value (<c>L.C</c>).</summary>
    public string? Created { get; set; }

    /// <summary>Raw modified value (<c>L.M</c>).</summary>
    public string? Modified { get; set; }

    /// <summary>Description (<c>L.D</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Volume label (<c>L.V</c>).</summary>
    public string? VolumeLabel { get; set; }

    /// <summary>Filesystem name (<c>L.I</c>).</summary>
    public string? FileSystem { get; set; }

    /// <summary>Raw media/drive type code (<c>L.T</c>).</summary>
    public string? MediaType { get; set; }

    /// <summary>Raw signed 32-bit volume serial (<c>L.E</c>).</summary>
    public string? VolumeSerial { get; set; }

    /// <summary>Raw capacity (<c>L.K</c>); <c>-1</c> is unavailable.</summary>
    public string? Capacity { get; set; }

    /// <summary>Raw free space (<c>L.R</c>); <c>-1</c> is unavailable.</summary>
    public string? FreeSpace { get; set; }

    /// <summary>Raw legacy options (<c>L.O</c>).</summary>
    public string? Options { get; set; }

    public string? ScanFlagA { get; set; }

    public string? ScanFlagB { get; set; }

    public string? ScanFlagC { get; set; }

    public string? ScanFlagD { get; set; }

    public string? ScanFlagE { get; set; }

    public string? ScanFlagF { get; set; }

    public string? ScanFlagG { get; set; }

    /// <summary>Cover member filename (<c>L.G</c>).</summary>
    public string? CoverMemberName { get; set; }

    /// <summary>Decompressed cover payload when present.</summary>
    public byte[]? Cover { get; set; }

    /// <summary>Keywords/tags (<c>L.W</c>); original text.</summary>
    public string? Keywords { get; set; }

    /// <summary>Storage location (<c>L.DS</c>).</summary>
    public string? StorageLocation { get; set; }

    /// <summary>Index code (<c>L.DI</c>).</summary>
    public string? IndexCode { get; set; }

    /// <summary>Barcode text (<c>L.DB</c>); never numeric.</summary>
    public string? Barcode { get; set; }

    /// <summary>Raw rating (<c>L.DR</c>).</summary>
    public string? Rating { get; set; }

    /// <summary>Disc directory/file tree.</summary>
    public ZdcDiscTree Tree { get; set; } = new();
}

/// <summary>Root <c>R</c> element of a disc tree member.</summary>
public sealed class ZdcDiscTree
{
    /// <summary>Ordered root entries.</summary>
    public List<ZdcTreeEntry> Entries { get; } = [];
}

/// <summary>A disc-tree directory (<c>D</c>) or file (<c>F</c>) entry.</summary>
public sealed class ZdcTreeEntry
{
    /// <summary>True for <c>D</c>; false for <c>F</c>.</summary>
    public bool IsDirectory { get; set; }

    /// <summary>Name (<c>N</c>); required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Raw Windows attribute bitmask (<c>A</c>).</summary>
    public string? Attributes { get; set; }

    /// <summary>Raw modified value (<c>M</c>).</summary>
    public string? Modified { get; set; }

    /// <summary>Raw created value (<c>C</c>).</summary>
    public string? Created { get; set; }

    /// <summary>Raw additional timestamp (<c>B</c>).</summary>
    public string? TimestampB { get; set; }

    /// <summary>Description (<c>D</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Keywords (<c>W</c>).</summary>
    public string? Keywords { get; set; }

    /// <summary>Raw file size (<c>S</c>); required for files.</summary>
    public string? Size { get; set; }

    /// <summary>Unknown attributes retained for round trip.</summary>
    public List<KeyValuePair<string, string>> ExtensionAttributes { get; } = [];

    /// <summary>Ordered child entries for directories.</summary>
    public List<ZdcTreeEntry> Children { get; } = [];
}
