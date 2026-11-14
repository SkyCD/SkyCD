using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Host side result of a plugin configuration flow.
/// </summary>
public sealed class PluginConfigurationOutcome
{
    /// <summary>
    /// Gets the outcome of the configuration flow.
    /// </summary>
    public required PluginConfigurationStatus Status { get; init; }

    /// <summary>
    /// Gets the settings that are persisted for the plugin.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets an optional failure description.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets whether the settings were accepted and persisted.
    /// </summary>
    public bool IsSuccess => Status == PluginConfigurationStatus.Succeeded;

    /// <summary>
    /// Gets whether the user dismissed the configuration surface.
    /// </summary>
    public bool IsCanceled => Status == PluginConfigurationStatus.Canceled;
}
