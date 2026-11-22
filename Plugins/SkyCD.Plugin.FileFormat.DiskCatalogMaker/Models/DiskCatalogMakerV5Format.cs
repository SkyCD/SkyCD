using System;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Container constants for the DiskCatalogMaker v5 catalog binary layout.
/// The layout is documented in docs/plugins/diskcatalogmaker-v5.md.
/// </summary>
public static class DiskCatalogMakerV5Format
{
    /// <summary>ASCII magic that starts every v5 catalog payload.</summary>
    public static ReadOnlySpan<byte> Magic => "DCM5"u8;

    /// <summary>Size of the fixed catalog header in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>Supported catalog format major version.</summary>
    public const ushort MajorVersion = 5;

    /// <summary>Little-endian marker stored in the header ("LE").</summary>
    public const ushort LittleEndianMarker = 0x4C45;

    /// <summary>Size of one table-directory entry in bytes.</summary>
    public const int DirectoryEntrySize = 20;

    /// <summary>Size of one table header in bytes.</summary>
    public const int TableHeaderSize = 16;

    /// <summary>Size of one volume data record in bytes.</summary>
    public const int VolumeDataRecordSize = 56;

    /// <summary>Size of one volume directory record in bytes.</summary>
    public const int VolumeDirRecordSize = 80;

    /// <summary>Size of one volume thumbnail record in bytes.</summary>
    public const int VolumeThumbnailRecordSize = 16;

    /// <summary>Size of the fixed portion of one volume alias record in bytes.</summary>
    public const int VolumeAliasRecordSize = 64;

    /// <summary>Size of the fixed portion of one string record in bytes.</summary>
    public const int VolumeNameRecordHeaderSize = 12;

    /// <summary>Table identifier for TDVolumeDataTableV5.</summary>
    public const uint VolumeDataTableId = 1;

    /// <summary>Table identifier for TDVolumeDirTableV5.</summary>
    public const uint VolumeDirTableId = 2;

    /// <summary>Table identifier for TDVolumeNameTableV5.</summary>
    public const uint VolumeNameTableId = 3;

    /// <summary>Table identifier for TDVolumeAliasTableV5.</summary>
    public const uint VolumeAliasTableId = 4;

    /// <summary>Table identifier for TDVolumeThumbnailTableV5.</summary>
    public const uint VolumeThumbnailTableId = 5;

    /// <summary>Table identifier for TDVolumeMutableStringTableV5.</summary>
    public const uint VolumeMutableStringTableId = 6;

    /// <summary>Header value required on every known table.</summary>
    public const uint KnownTableHeaderValue = 1;

    /// <summary>Parent item identifier used for items directly below a volume root.</summary>
    public const uint NoParentItemId = uint.MaxValue;

    /// <summary>Maximum catalog payload the importer materializes in memory.</summary>
    public const long MaximumCatalogBytes = 512L * 1024 * 1024;

    /// <summary>Maximum number of tables accepted from the directory.</summary>
    public const int MaximumTableCount = 4096;

    /// <summary>Maximum record count accepted from one table.</summary>
    public const int MaximumRecordCount = 5_000_000;

    /// <summary>Maximum byte length accepted for a single catalog string.</summary>
    public const int MaximumStringBytes = 4 * 1024 * 1024;

    /// <summary>Maximum alias "extra" payload retained; larger payloads are skipped with a warning.</summary>
    public const int MaximumAliasExtraBytes = 64 * 1024;

    /// <summary>String encoding tag for strict UTF-8.</summary>
    public const ushort Utf8Encoding = 0;

    /// <summary>String encoding tag for strict UTF-16 little-endian.</summary>
    public const ushort Utf16LittleEndianEncoding = 1;
}
