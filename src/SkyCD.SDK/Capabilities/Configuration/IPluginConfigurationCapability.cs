using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Configuration;

/// <summary>
/// Capability contract for plugins that expose one or more configuration pages to the host's Options dialog.
/// </summary>
public interface IPluginConfigurationCapability : IPluginCapability
{
    /// <summary>
    /// Gets the descriptors of the contributed configuration pages in sidebar order.
    /// </summary>
    IReadOnlyList<PluginConfigurationDescriptor> Configurations { get; }

    /// <summary>
    /// Creates an isolated draft of the requested page. Must not open a window, save settings or change
    /// consumers; any plugin-side work belongs to the page's accept callback.
    /// </summary>
    Task<PluginConfigurationPage> CreatePageAsync(
        PluginConfigurationRequest request,
        CancellationToken cancellationToken = default);
}
