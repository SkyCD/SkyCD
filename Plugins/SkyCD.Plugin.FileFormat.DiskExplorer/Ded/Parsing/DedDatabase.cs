using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

/// <summary>
/// In-memory Disk Explorer database: fixed header values plus the ordered object
/// graph. Header flags, reserved flags, creation time, and opaque object data are
/// preserved across rewrites.
/// </summary>
public sealed class DedDatabase
{
    /// <summary>Gets or sets the format version; always <see cref="DedFormat.CurrentVersion"/> when written.</summary>
    public uint FormatVersion { get; set; } = DedFormat.CurrentVersion;

    /// <summary>Gets or sets preserved header flags.</summary>
    public uint HeaderFlags { get; set; }

    /// <summary>Gets or sets preserved reserved header flags.</summary>
    public uint ReservedFlags { get; set; }

    /// <summary>Gets or sets the creation FILETIME.</summary>
    public ulong CreationFileTime { get; set; }

    /// <summary>Gets or sets the ANSI title/comment text.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the preferred offset-table slot count. Writers use at least
    /// <see cref="DedFormat.InitialTableSlots"/> slots regardless.
    /// </summary>
    public int TableSlotCount { get; set; }

    /// <summary>Gets the object graph in serialization order.</summary>
    public List<DedCatalogObject> Objects { get; } = [];

    /// <summary>Finds an object by id, or null when it is absent.</summary>
    public DedCatalogObject? Find(ulong objectId)
    {
        foreach (var item in Objects)
        {
            if (item.ObjectId == objectId)
            {
                return item;
            }
        }

        return null;
    }
}
