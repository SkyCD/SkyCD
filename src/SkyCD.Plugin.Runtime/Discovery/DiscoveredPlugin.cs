using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Documents;
namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Runtime wrapper for a loaded plugin and discovered capabilities.
/// </summary>
public sealed class DiscoveredPlugin
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public PluginAuthorDocument? Author { get; init; }

    public string? ProjectUrl { get; init; }

    public required Version Version { get; init; }

    public required Version MinHostVersion { get; init; }

    public Version? MaxHostVersion { get; init; }

    public string Description { get; init; } = string.Empty;

    public required string FileName { get; init; }

    public required IReadOnlyCollection<IPluginCapability> Capabilities { get; init; }
}
