using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Configuration;

/// <summary>
/// Result payload for plugin configuration operations.
/// </summary>
public sealed class PluginConfigurationResult
{
    /// <summary>
    /// Gets whether the configuration flow completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets whether the configuration surface was dismissed without accepting changes.
    /// </summary>
    public bool Canceled { get; init; }

    /// <summary>
    /// Gets the settings accepted by the user. Empty when the flow was canceled.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets an optional failure message.
    /// </summary>
    public string? Error { get; init; }
}
