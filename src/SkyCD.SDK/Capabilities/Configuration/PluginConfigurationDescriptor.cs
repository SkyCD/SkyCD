namespace SkyCD.Plugin.Abstractions.Capabilities.Configuration;

/// <summary>
/// Describes a configuration surface contributed by a plugin.
/// </summary>
/// <param name="ConfigurationId">Stable identifier of the configuration page.</param>
/// <param name="Title">Page title shown inside the Options dialog.</param>
/// <param name="Group">Optional parent group for the page; <see langword="null"/> keeps it top-level.</param>
public sealed record PluginConfigurationDescriptor(
    string ConfigurationId,
    string Title,
    string? Group = null);
