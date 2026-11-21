namespace SkyCD.Plugin.Abstractions.Capabilities.Configuration;

/// <summary>
/// Describes a configuration surface contributed by a plugin.
/// </summary>
public sealed record PluginConfigurationDescriptor(
    string ConfigurationId,
    string Title);
