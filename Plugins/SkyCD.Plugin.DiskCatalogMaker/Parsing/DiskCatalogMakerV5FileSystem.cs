namespace SkyCD.Plugin.DiskCatalogMaker.Parsing;

/// <summary>
/// File system identifiers stored in the v5 volume data table.
/// </summary>
public enum DiskCatalogMakerV5FileSystem
{
    Unknown = 0,
    Hfs = 1,
    HfsPlus = 2,
    Apfs = 3,
    Fat = 4,
    Ntfs = 5,
    Udf = 6,
    Iso9660 = 7,
    Network = 8
}
