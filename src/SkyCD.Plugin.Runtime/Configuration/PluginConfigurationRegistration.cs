using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Registration of a plugin that contributes a configuration surface.
/// </summary>
public sealed record PluginConfigurationRegistration(
    string PluginId,
    string PluginName,
    PluginConfigurationDescriptor Descriptor);
