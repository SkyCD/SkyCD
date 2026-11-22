using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SkyCD.Plugin.FileFormat.Cathy.Models;

/// <summary>
/// Report document generated during Cathy catalog import.
/// </summary>
public sealed class CathyReportDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "cathyReport";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("volumeName")]
    public string VolumeName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public short Version { get; set; }

    [JsonPropertyName("totalFolders")]
    public int TotalFolders { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("freeSizeMb")]
    public uint FreeSizeMb { get; set; }

    [JsonPropertyName("isArchive")]
    public bool IsArchive { get; set; }

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<string> Diagnostics { get; set; } = [];

    [JsonPropertyName("unsupportedFeatures")]
    public IReadOnlyList<string> UnsupportedFeatures { get; set; } = [];
}
