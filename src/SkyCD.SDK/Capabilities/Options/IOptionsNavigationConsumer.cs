using System;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Options;

/// <summary>Receives navigation to the configuration pages a plugin contributed to the Options dialog.</summary>
public interface IOptionsNavigationConsumer : IPluginCapability
{
    /// <summary>
    /// Receives a callback that opens the plugin configuration page with the given
    /// <see cref="Configuration.PluginConfigurationDescriptor.ConfigurationId"/> and completes when Options closes.
    /// </summary>
    void InitializeOptionsNavigation(Func<string, Task> openConfigurationAsync);
}
