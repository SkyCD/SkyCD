namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Entry kind a scan rule applies to, so a file-only rule never excludes a folder with the same name.
/// </summary>
public enum ScanEntryType
{
    File = 0,

    Folder = 1,

    Both = 2
}
