namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>A location name was blank.</summary>
public sealed class LocationApiNameRequiredException() : LocationApiException("A location name is required.");
