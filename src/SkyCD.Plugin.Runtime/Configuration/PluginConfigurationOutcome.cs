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

    /// <summary>Gets the newly created draft, owned by the caller until disposal.</summary>
    public SkyCD.SDK.Capabilities.Configuration.PluginConfigurationPage? Page { get; init; }

    /// <summary>
    /// Gets the validated settings snapshot. Creation and acceptance do not persist this snapshot.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets an optional failure description.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets whether the requested operation succeeded; persistence is a separate host action.
    /// </summary>
    public bool IsSuccess => Status == PluginConfigurationStatus.Succeeded;
}
