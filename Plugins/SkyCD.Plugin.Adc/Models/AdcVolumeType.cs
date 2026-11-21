namespace SkyCD.Plugin.Adc.Models;

/// <summary>
/// Volume/media type stored on an Advanced Disk Catalog volume record.
/// </summary>
public enum AdcVolumeType : byte
{
    Unknown = 0,
    Fixed = 1,
    Removable = 2,
    Network = 3,
    CdRom = 4,
    Dvd = 5,
    RamDisk = 6
}
