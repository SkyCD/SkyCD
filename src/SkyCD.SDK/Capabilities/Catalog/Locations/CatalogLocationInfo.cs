namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>
/// One location definition with the number of catalog entries whose effective location it is.
/// </summary>
public sealed record CatalogLocationInfo(string Id, string Name, int UsageCount);
