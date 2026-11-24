namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>
/// Optional capability hook the host calls, mirroring <c>IOAuthClient.InitializeOAuth</c>, to hand a
/// capability the catalog API scoped to the host session before its services are published.
/// </summary>
public interface ICatalogApiConsumer : IPluginCapability
{
    /// <summary>Receives the read-only catalog API for the host process lifetime.</summary>
    void InitializeCatalogApi(ICatalogApi api);
}
