using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Configuration;

/// <summary>
/// Capability contract for plugins that expose a configuration surface to the host.
/// </summary>
public interface IPluginConfigurationCapability : IPluginCapability
{
    /// <summary>
    /// Gets the descriptor of the contributed configuration surface.
    /// </summary>
    PluginConfigurationDescriptor Configuration { get; }

    /// <summary>
    /// Opens the plugin configuration surface and returns the settings accepted by the user.
    /// </summary>
    Task<PluginConfigurationResult> ConfigureAsync(
        PluginConfigurationRequest request,
        CancellationToken cancellationToken = default);
}
