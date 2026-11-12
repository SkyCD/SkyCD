namespace SkyCD.Plugin.WinCatalog.Csv;

/// <summary>
/// Semantic column types recognized in WinCatalog CSV report exports.
/// </summary>
public enum WinCatalogCsvColumn
{
    Unknown = 0,
    CatalogPath,
    FilePath,
    Name,
    Filename,
    Disk,
    VolumeLabel,
    Size,
    Type,
    Ext,
    Created,
    Modified,
    Comments,
    Tags,
    Rating,
    MD5,
    SHA256,
    Blake2,
    CRC32,
    PhysicalLocation
}
