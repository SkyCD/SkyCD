namespace SkyCD.Plugin.DiskCatalogMaker.Models;

/// <summary>
/// Semantic column types recognized in DiskCatalogMaker text-export reports.
/// </summary>
public enum DiskCatalogMakerColumn
{
    Unknown = 0,
    Path,
    Name,
    Size,
    Kind,
    DateModified,
    DateCreated,
    Creator,
    TypeCode,
    Comments,
    Label,
    Disk
}
