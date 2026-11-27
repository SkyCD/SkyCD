namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>
/// Optional capability hook the host calls to hand a plugin the storage-location API before its services
/// are published. The host pushes a fresh instance after every plugin runtime synchronisation.
/// </summary>
public interface ILocationApiConsumer : IPluginCapability
{
    /// <summary>Receives the location API for the host process lifetime.</summary>
    void InitializeLocationApi(ILocationApi api);
}
