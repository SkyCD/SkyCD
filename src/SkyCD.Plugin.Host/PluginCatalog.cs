using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Host;

/// <summary>
/// In-memory plugin catalog used by host services.
/// </summary>
public sealed class PluginCatalog
{
    private readonly List<DiscoveredPlugin> _plugins = [];

    public IReadOnlyCollection<DiscoveredPlugin> Plugins => _plugins;

    public void SetPlugins(IEnumerable<DiscoveredPlugin> discovered)
    {
        _plugins.Clear();
        _plugins.AddRange(discovered);
    }

    public IReadOnlyList<TCapability> GetCapabilities<TCapability>()
        where TCapability : class, IPluginCapability
    {
        return _plugins
            .SelectMany(plugin => plugin.Capabilities)
            .OfType<TCapability>()
            .ToList();
    }
}
