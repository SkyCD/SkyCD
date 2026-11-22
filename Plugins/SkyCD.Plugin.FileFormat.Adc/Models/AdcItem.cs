using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Adc.Models;

/// <summary>
/// A cataloged file or folder. Parent references are resolved after the whole record
/// stream is read; see <c>FORMAT.md</c> for the parent-reference rules.
/// </summary>
public sealed class AdcItem
{
    public uint ItemId { get; set; }

    /// <summary>
    /// Gets or sets the parent item identifier, or <c>0</c> for a direct child of the volume.
    /// </summary>
    public uint ParentId { get; set; }

    public uint VolumeId { get; set; }

    public AdcItemKind Kind { get; set; }

    public byte Attributes { get; set; }

    public uint TypeCode { get; set; }

    public ulong SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the creation time as a Windows FILETIME value, or <c>0</c> when absent.
    /// </summary>
    public ulong CreatedFileTime { get; set; }

    public ulong ModifiedFileTime { get; set; }

    public ulong AccessedFileTime { get; set; }

    /// <summary>
    /// Gets or sets the one-based comment index, or <c>0</c> when the item has no comment.
    /// </summary>
    public uint CommentIndex { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<uint> CategoryIds { get; } = [];

    public List<AdcProperty> Properties { get; } = [];
}
