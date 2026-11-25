using SkyCD.SDK.Capabilities.Storage;

namespace SkyCD.Presentation.Catalog;

/// <summary>
/// Creates plugin-scoped storage views over the open catalog database, so each plugin writes into its own
/// namespaced collections and reuses them across releases.
/// </summary>
public sealed class PluginStorageApiFactory(OpenCatalogState state)
{
    /// <summary>Creates the storage API for one plugin identity.</summary>
    public IPluginStorageApi ForPlugin(string pluginId)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new PluginStorageApi(state, pluginId);
    }
}
