using System;
using System.Collections.Generic;
using SkyCD.Plugin.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.DiskExplorer.Ded.Models;

/// <summary>
/// One serialized catalog object. Identity, order, flags, extensions, and
/// payload bytes are preserved so unmodified objects survive a read/write cycle.
/// </summary>
public sealed class DedCatalogObject
{
    private uint rawKind = (uint)DedObjectKind.File;

    /// <summary>Gets or sets the wire kind value. Unknown values are retained.</summary>
    public uint RawKind
    {
        get => rawKind;
        set => rawKind = value;
    }

    /// <summary>Gets or sets the classified kind derived from <see cref="RawKind"/>.</summary>
    public DedObjectKind Kind
    {
        get => DedObjectKinds.IsKnownValue(rawKind) ? (DedObjectKind)rawKind : DedObjectKind.Unknown;
        set => rawKind = (uint)value;
    }

    /// <summary>Gets or sets the object id; unique within a database and never zero.</summary>
    public ulong ObjectId { get; set; }

    /// <summary>Gets or sets the parent object id; zero means no parent.</summary>
    public ulong ParentId { get; set; }

    /// <summary>Gets or sets preserved per-object flags.</summary>
    public uint Flags { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public DedText Name { get; set; } = DedText.Empty;

    /// <summary>Gets or sets the source/path text.</summary>
    public DedText Path { get; set; } = DedText.Empty;

    /// <summary>Gets or sets the description or comment.</summary>
    public DedText Description { get; set; } = DedText.Empty;

    /// <summary>Gets or sets the unsigned 64-bit logical size.</summary>
    public ulong LogicalSize { get; set; }

    /// <summary>Gets or sets the unsigned 64-bit aggregate size.</summary>
    public ulong AggregateSize { get; set; }

    /// <summary>Gets or sets the raw creation FILETIME (100ns since 1601-01-01 UTC).</summary>
    public ulong CreatedFileTime { get; set; }

    /// <summary>Gets or sets the raw last-write FILETIME.</summary>
    public ulong ModifiedFileTime { get; set; }

    /// <summary>Gets or sets the raw catalog-entry FILETIME.</summary>
    public ulong AddedFileTime { get; set; }

    /// <summary>Gets or sets the child file count.</summary>
    public uint FileCount { get; set; }

    /// <summary>Gets or sets the child folder count.</summary>
    public uint FolderCount { get; set; }

    /// <summary>Gets ordered child object ids.</summary>
    public List<ulong> ChildIds { get; } = [];

    /// <summary>Gets ordered category object ids this object belongs to.</summary>
    public List<ulong> CategoryIds { get; } = [];

    /// <summary>Gets tagged extension fields; unknown tags are preserved.</summary>
    public List<DedExtensionField> Extensions { get; } = [];

    /// <summary>Gets opaque payloads; unknown kinds are preserved.</summary>
    public List<DedPayload> Payloads { get; } = [];

    /// <summary>Gets or sets opaque trailing record bytes not otherwise interpreted.</summary>
    public byte[]? Tail { get; set; }

    /// <summary>
    /// Gets or sets the verbatim record bytes for an unknown object kind. Known
    /// kinds never use this; unknown kinds are re-emitted unchanged.
    /// </summary>
    public byte[]? RawRecord { get; set; }

    public bool IsUnknownKind => Kind == DedObjectKind.Unknown;
}
