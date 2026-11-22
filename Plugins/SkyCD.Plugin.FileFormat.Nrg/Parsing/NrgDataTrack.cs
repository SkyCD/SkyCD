namespace SkyCD.Plugin.FileFormat.Nrg.Parsing;

/// <summary>
/// Byte layout of one data track inside an NRG image. The track is a sequence of fixed-size sectors;
/// only the 2048-byte user-data area of each sector belongs to the ISO 9660 filesystem.
/// </summary>
internal sealed record NrgDataTrack(long StartOffset, long EndOffset, int SectorSize, int DataOffsetInSector)
{
    /// <summary>Size of the user-data area exposed to the ISO 9660 reader.</summary>
    public const int UserDataSize = 2048;

    /// <summary>Number of whole sectors stored in the track.</summary>
    public long SectorCount => (EndOffset - StartOffset) / SectorSize;

    /// <summary>Length of the user-data view of the track.</summary>
    public long Length => SectorCount * UserDataSize;
}
