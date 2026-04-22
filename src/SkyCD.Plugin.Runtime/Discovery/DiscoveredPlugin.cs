using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
///     Runtime wrapper for a loaded plugin and discovered capabilities.
/// </summary>
public sealed class DiscoveredPlugin
{
    public required IPlugin Plugin { get; init; }

    public required IReadOnlyCollection<IPluginCapability> Capabilities { get; init; }
}