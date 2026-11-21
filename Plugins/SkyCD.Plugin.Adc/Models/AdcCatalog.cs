using System.Collections.Generic;

namespace SkyCD.Plugin.Adc.Models;

/// <summary>
/// A fully validated Advanced Disk Catalog database.
/// </summary>
public sealed class AdcCatalog
{
    public ushort VersionMajor { get; set; }

    public ushort VersionMinor { get; set; }

    public uint Flags { get; set; }

    public int TotalRecordCount { get; set; }

    public List<AdcVolume> Volumes { get; } = [];

    public List<AdcItem> Items { get; } = [];

    public List<AdcCategory> Categories { get; } = [];

    /// <summary>
    /// Gets the comment table in file order. Item comment indexes are one-based into this list.
    /// </summary>
    public List<string> Comments { get; } = [];

    /// <summary>
    /// Gets the unknown record types and property descriptors encountered while reading.
    /// </summary>
    public List<string> UnsupportedFields { get; } = [];
}
