namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Describes a plugin package and its compatibility requirements.
/// </summary>
/// <param name="Id">Stable plugin identifier.</param>
/// <param name="DisplayName">Human-readable plugin name.</param>
/// <param name="Version">Plugin semantic version.</param>
/// <param name="MinHostVersion">Minimum host version required by the plugin.</param>
/// <param name="Description">Optional plugin description.</param>
public sealed record PluginDescriptor(
    string Id,
    string DisplayName,
    Version Version,
    Version MinHostVersion,
    string? Description = null);
