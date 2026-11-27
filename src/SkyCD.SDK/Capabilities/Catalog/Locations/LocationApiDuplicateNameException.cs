namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>Another location already uses the requested name, ignoring case.</summary>
public sealed class LocationApiDuplicateNameException(string name)
    : LocationApiException($"A location named '{name}' already exists.")
{
    /// <summary>The requested location name.</summary>
    public string Name { get; } = name;
}
