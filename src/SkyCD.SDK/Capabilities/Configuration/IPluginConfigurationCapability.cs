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
    /// Creates an isolated Options draft. Must not open a window, save settings or change consumers.
    /// </summary>
    Task<PluginConfigurationPage> CreatePageAsync(
        PluginConfigurationRequest request,
        CancellationToken cancellationToken = default);
}
