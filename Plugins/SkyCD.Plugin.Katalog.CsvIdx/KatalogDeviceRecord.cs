using System;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// A device or virtual group record parsed from device.csv.
/// </summary>
public sealed class KatalogDeviceRecord
{
    public int DeviceId { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Catalog", "Storage", "Group", etc.
    public int ExternalId { get; set; }
    public string Path { get; set; } = string.Empty;
    public long TotalFileSize { get; set; }
    public long TotalFileCount { get; set; }
    public long TotalSpace { get; set; }
    public long FreeSpace { get; set; }
    public bool Active { get; set; }
    public int GroupId { get; set; }
    public string DateUpdated { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Comment { get; set; } = string.Empty;
}
