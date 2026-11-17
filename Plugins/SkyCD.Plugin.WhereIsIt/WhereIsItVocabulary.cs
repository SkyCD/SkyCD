namespace SkyCD.Plugin.WhereIsIt;

/// <summary>
/// Entry roles in a Where Is It? XML report. Only File and Folder carry catalog
/// data; Disk-typed items mark an incompatible report kind.
/// </summary>
public enum WhereIsItElementKind
{
    Unknown,
    File,
    Folder,
    Disk
}

/// <summary>
/// Recognized report fields. Everything else is retained as extension metadata.
/// </summary>
public enum WhereIsItField
{
    Unknown,
    Name,
    Extension,
    Size,
    Date,
    Time,
    Path,
    DiskName,
    DiskType,
    DiskNumber,
    DiskLocation,
    Crc,
    Category,
    Flag,
    Description
}
