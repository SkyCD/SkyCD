using System.Collections.Generic;

namespace SkyCD.Plugin.CdCatalogExpert;

/// <summary>
/// In-memory model of a CD Catalog Expert 9.x container.
/// </summary>
public sealed class ZdcDatabase
{
    /// <summary>Container checksum/profile value at <c>0x120</c>.</summary>
    public uint Csc { get; set; }

    /// <summary>Container checksum/profile value at <c>0x124</c>.</summary>
    public uint Oof { get; set; }

    /// <summary>Profile values <c>a</c>..<c>e</c> at <c>0x12C</c>; preserved.</summary>
    public uint[] ProfileValues { get; } = new uint[5];

    /// <summary>Reserved/profile data between the magics; preserved verbatim.</summary>
    public byte[] ReservedProfile { get; set; } = new byte[ZdcFormat.ReservedProfileSize];

    /// <summary>Container key/reserved data; all zero for unencrypted ZDC files.</summary>
    public byte[] ContainerKey { get; set; } = new byte[ZdcFormat.ContainerKeySize];

    public List<ZdcMember> Members { get; } = [];

    /// <summary>
    /// Directory-tree chunk reserved words in chain order, one entry per
    /// visited chunk; restored verbatim on write for unchanged catalogs.
    /// </summary>
    public List<uint[]> TreeReserved { get; } = [];

    internal int PendingFirstTreeChunk { get; set; }

    internal int PendingEntryCount { get; set; }
}

/// <summary>
/// One 512-byte directory-tree member record with its decompressed payload.
/// </summary>
public sealed class ZdcMember
{
    /// <summary>First file-index chunk; <c>-1</c> means the member is empty.</summary>
    public int FirstFileIndexChunk { get; set; } = -1;

    /// <summary>Stored member length, AACS wrapper included.</summary>
    public ulong StoredLength { get; set; }

    /// <summary>Timestamps A/B/C in SFS 100-nanosecond-like units; raw values.</summary>
    public ulong TimestampA { get; set; }

    public ulong TimestampB { get; set; }

    public ulong TimestampC { get; set; }

    /// <summary>File type/flags at record offset <c>0x024</c>.</summary>
    public uint FileType { get; set; }

    /// <summary>Parent entry; <c>-1</c> for root members in ZDC.</summary>
    public int ParentEntry { get; set; } = -1;

    /// <summary>Per-file key; zero for unencrypted ZDC members.</summary>
    public byte[] FileKey { get; set; } = new byte[ZdcFormat.MemberKeySize];

    /// <summary>Reserved bytes; preserved verbatim.</summary>
    public byte[] Reserved { get; set; } = new byte[ZdcFormat.MemberReservedSize];

    /// <summary>Entry type/flags at record offset <c>0x0DC</c>.</summary>
    public uint EntryType { get; set; }

    /// <summary>Decoded ASCII member file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Raw ASCII member file name bytes (without terminator).</summary>
    public byte[] FileNameBytes { get; set; } = [];

    /// <summary>AACS profile value from the member payload envelope.</summary>
    public uint AacsProfile { get; set; }

    /// <summary>Decompressed member payload bytes.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>
    /// File-index chunk reserved words in chain order; restored verbatim on
    /// write for unchanged catalogs.
    /// </summary>
    public List<uint[]> IndexReserved { get; } = [];

    /// <summary>
    /// File-data chunk headers in reference order; restored verbatim on write
    /// for unchanged catalogs.
    /// </summary>
    public List<ZdcDataChunkHeader> DataHeaders { get; } = [];
}

/// <summary>
/// Preserved file-data chunk header: link value, flags, and reserved bytes.
/// The checksum is recomputed on write.
/// </summary>
/// <param name="Link">Reserved/link value at chunk offset <c>0x00</c>.</param>
/// <param name="Flags">Chunk flags at offset <c>0x08</c>.</param>
/// <param name="Reserved">Reserved bytes at offset <c>0x0C</c>.</param>
public sealed record ZdcDataChunkHeader(int Link, uint Flags, byte[] Reserved);
