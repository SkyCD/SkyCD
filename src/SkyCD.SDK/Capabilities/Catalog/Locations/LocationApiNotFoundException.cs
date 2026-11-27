namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>The referenced location does not exist.</summary>
public sealed class LocationApiNotFoundException(string locationId)
    : LocationApiException($"Location '{locationId}' does not exist.")
{
    /// <summary>The requested location id.</summary>
    public string LocationId { get; } = locationId;
}
