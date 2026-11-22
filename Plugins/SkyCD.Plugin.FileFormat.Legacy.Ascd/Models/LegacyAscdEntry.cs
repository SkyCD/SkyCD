namespace SkyCD.Plugin.FileFormat.Legacy.Ascd.Models;

public sealed class LegacyAscdEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string ParentId { get; init; }

    public required string Type { get; init; }

    public string PropertiesXml { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string ApplicationId { get; init; } = "<?Application_ID?>";
}