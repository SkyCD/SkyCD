namespace SkyCD.Plugin.DiskExplorer;

/// <summary>
/// Entry roles in a Disk Explorer Professional XML report.
/// </summary>
public enum DiskExplorerElementKind
{
    Unknown,
    Volume,
    CategoryFolder,
    Folder,
    Archive,
    File
}

/// <summary>
/// Recognized report fields. Everything else is retained as extension metadata.
/// </summary>
public enum DiskExplorerField
{
    Unknown,
    Name,
    Files,
    Folders,
    Size,
    Date,
    Time,
    Type,
    Description
}
