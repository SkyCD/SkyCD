using SkyCD.Plugin.Abstractions.Capabilities;

namespace SkyCD.Plugin.Abstractions.OAuth;

/// <summary>Optional capability hook called by the host before publishing discovered plugin services.</summary>
public interface IOAuthClient : IPluginCapability
{
    /// <summary>Receives the OAuth service scoped to this plugin's assembly identity.</summary>
    void InitializeOAuth(IOAuthService service);
}
