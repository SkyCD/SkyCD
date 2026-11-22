namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Physical access characteristics of a scan root, used to pick a safe initial worker count.
/// </summary>
public enum ScanSourceKind
{
    Unknown,
    SlowRemovable,
    Optical,
    HardDisk,
    SolidState,
    Network
}
