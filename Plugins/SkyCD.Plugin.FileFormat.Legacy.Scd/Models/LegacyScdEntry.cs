namespace SkyCD.Plugin.FileFormat.Legacy.Scd.Models;

public sealed class LegacyScdEntry
{
    public required string Path { get; init; }

    public long? SizeBytes { get; init; }

    public bool IsDirectory { get; init; }
}
