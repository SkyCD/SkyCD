namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

public sealed class LegacyScdEntry
{
    public required string Path { get; init; }

    public long? SizeBytes { get; init; }

    public bool IsDirectory { get; init; }
}
