using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;

/// <summary>
/// Container constants for Disk Explorer Professional <c>*.ded</c> databases.
/// All multibyte values are little-endian. See <c>docs/plugins/disk-explorer-ded.md</c>.
/// </summary>
public static class DedFormat
{
    /// <summary>ASCII magic at offset 0x00.</summary>
    public static ReadOnlySpan<byte> Magic => "DEDB"u8;

    /// <summary>Fixed header size in bytes.</summary>
    public const int HeaderSize = 0x50;

    /// <summary>Format version written by this implementation (v0x102).</summary>
    public const uint CurrentVersion = 0x00000102;

    /// <summary>Header creation FILETIME offset.</summary>
    public const int CreationTimeOffset = 0x14;

    /// <summary>Header flags offset (preserved on rewrite).</summary>
    public const int FlagsOffset = 0x10;

    /// <summary>Header reserved flags offset (preserved on rewrite).</summary>
    public const int ReservedFlagsOffset = 0x1C;

    /// <summary>Title/comment field offset.</summary>
    public const int TitleOffset = 0x20;

    /// <summary>Title/comment field size; NUL-terminated, zero padded.</summary>
    public const int TitleSize = 48;

    /// <summary>Number of offset-table slots new files reserve.</summary>
    public const int InitialTableSlots = 1024;

    /// <summary>Header flag bit marking a password-protected database.</summary>
    public const uint HeaderFlagPasswordProtected = 0x00000001;

    /// <summary>Size of the shared object record prefix (kind, flags, record length).</summary>
    public const int ObjectPrefixSize = 12;

    /// <summary>Minimum serialized size of a known object record.</summary>
    public const int MinimumKnownRecordSize = 96;

    /// <summary>Highest possible object offset because table entries are 32-bit.</summary>
    public const uint MaximumFileSize = uint.MaxValue;
}
