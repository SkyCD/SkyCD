using System.Collections.Generic;

namespace SkyCD.Plugin.BrokenX;

/// <summary>
/// A database item record with its preserved raw fields and children.
/// </summary>
public sealed class BxdItem
{
    public required ushort Flags { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the original encoded name bytes for lossless rewriting.
    /// </summary>
    public byte[]? NameBytes { get; set; }

    public uint Field14 { get; set; }

    public uint Field18 { get; set; }

    public List<BxdProperty> Metadata { get; } = [];

    public List<BxdItem> Children { get; } = [];
}
