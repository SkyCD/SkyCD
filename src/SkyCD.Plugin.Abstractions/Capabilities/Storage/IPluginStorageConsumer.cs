namespace SkyCD.Plugin.Abstractions.Capabilities.Storage;

/// <summary>
/// Optional capability hook the host calls, mirroring <c>IOAuthClient.InitializeOAuth</c>, to give a plugin a
/// storage API scoped to its own identity before its services are published.
/// </summary>
public interface IPluginStorageConsumer : IPluginCapability
{
    /// <summary>Receives the plugin-scoped storage API for the host process lifetime.</summary>
    void InitializeStorage(IPluginStorageApi storage);
}
