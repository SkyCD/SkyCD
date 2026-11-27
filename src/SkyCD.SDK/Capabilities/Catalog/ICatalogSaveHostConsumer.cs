namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>
/// Optional capability hook the host calls to give a plugin the catalog save surface before its services are
/// published, so the plugin writes through the loaded formats instead of referencing host assemblies.
/// </summary>
public interface ICatalogSaveHostConsumer : IPluginCapability
{
    /// <summary>Receives the host catalog save API for the host process lifetime.</summary>
    void InitializeCatalogSaveHost(ICatalogSaveHostApi hostApi);
}
