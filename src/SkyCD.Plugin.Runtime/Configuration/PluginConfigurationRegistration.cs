using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Registration of one configuration page contributed by a plugin.
/// </summary>
/// <param name="PluginId">Identity of the contributing plugin.</param>
/// <param name="PluginName">Display name of the contributing plugin.</param>
/// <param name="Descriptor">Descriptor of this page.</param>
/// <param name="DescriptorIndex">Position of the descriptor in the capability's list.</param>
public sealed record PluginConfigurationRegistration(
    string PluginId,
    string PluginName,
    PluginConfigurationDescriptor Descriptor,
    int DescriptorIndex = 0);
