namespace SkyCD.Plugin.Legacy.Scd;

public sealed class LegacyScdEntry
{
    public required string Path { get; init; }

    public long? SizeBytes { get; init; }
}
