namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>Location data is unavailable because no catalog is open.</summary>
public sealed class LocationApiCatalogUnavailableException()
    : LocationApiException("Locations are unavailable because no catalog is open.");
