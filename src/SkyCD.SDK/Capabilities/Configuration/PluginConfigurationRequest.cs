using System;
using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Configuration;

/// <summary>
/// Request payload for opening a plugin configuration surface.
/// </summary>
public sealed class PluginConfigurationRequest
{
    /// <summary>
    /// Gets the configuration identifier the plugin is expected to handle.
    /// </summary>
    public required string ConfigurationId { get; init; }

    /// <summary>
    /// Gets the settings currently persisted for the plugin.
    /// </summary>
    public IReadOnlyDictionary<string, string?> CurrentSettings { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
