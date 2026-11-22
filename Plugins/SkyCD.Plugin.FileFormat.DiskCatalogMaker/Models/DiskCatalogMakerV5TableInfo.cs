namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

/// <summary>
/// Location and header information for one table listed in the v5 directory.
/// </summary>
public sealed record DiskCatalogMakerV5TableInfo(
    uint TableId,
    uint HeaderValue,
    int Offset,
    int Length,
    uint RecordCount,
    bool IsKnown);
