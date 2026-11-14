using System;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// A storage record parsed from storage.csv.
/// </summary>
public sealed class KatalogStorageRecord
{
    public int StorageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long Total { get; set; }
    public long Free { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string BuildDate { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string PicturePath { get; set; } = string.Empty;
    public int UserId { get; set; }
}

/// <summary>
/// A tag record parsed from tags.csv.
/// </summary>
public sealed class KatalogTagRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DateTime { get; set; } = string.Empty;
}
