namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>A location name was blank.</summary>
public sealed class LocationApiNameRequiredException() : LocationApiException("A location name is required.");
