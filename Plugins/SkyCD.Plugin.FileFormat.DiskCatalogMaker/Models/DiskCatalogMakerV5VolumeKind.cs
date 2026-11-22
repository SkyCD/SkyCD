namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Volume kinds stored in the v5 volume data table.
/// </summary>
public enum DiskCatalogMakerV5VolumeKind
{
    Unknown = 0,
    Disk = 1,
    CompactDisc = 2,
    Dvd = 3,
    Network = 4,
    Folder = 5,
    DiskImage = 6
}
