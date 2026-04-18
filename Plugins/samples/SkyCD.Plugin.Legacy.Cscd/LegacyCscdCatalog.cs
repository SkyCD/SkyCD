namespace SkyCD.Plugin.Legacy.Cscd;

public sealed class LegacyCscdCatalog
{
    public List<LegacyCscdEntry> Entries { get; } = [];
}

public sealed class LegacyCscdEntry
{
    public required string Path { get; init; }

    public long? SizeBytes { get; init; }
}
