namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>
/// Optional capability the host notifies when the catalog opens, closes or changes, so a feature plugin
/// can warm or drop caches and request a refresh of its contributions.
/// </summary>
public interface ICatalogLifecyclePluginCapability : IPluginCapability
{
    /// <summary>Receives a catalog lifecycle transition. <paramref name="catalogPath"/> is null on close.</summary>
    void OnCatalogLifecycle(CatalogLifecycle lifecycle, string? catalogPath);
}
