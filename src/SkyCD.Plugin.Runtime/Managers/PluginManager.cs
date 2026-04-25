using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Loading;

namespace SkyCD.Plugin.Runtime.Managers;

/// <summary>
/// Unified plugin manager for discovery, loading, and capability lookup.
/// </summary>
public sealed class PluginManager
{
    private readonly AssembliesListFactory _assembliesListFactory = new();
    private readonly DiscoveredPluginFactory _discoveredPluginFactory = new();
    private readonly List<DiscoveredPlugin> _plugins = [];
    private readonly List<PluginLoadDiagnostic> _diagnostics = [];

    public IReadOnlyCollection<DiscoveredPlugin> Plugins => _plugins;
    public IReadOnlyCollection<PluginLoadDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<TCapability> GetCapabilities<TCapability>()
        where TCapability : class, IPluginCapability
    {
        return _plugins
            .SelectMany(plugin => plugin.Capabilities)
            .OfType<TCapability>()
            .ToList();
    }

    public void Discover(string? pluginDirectory, Version hostVersion)
    {
        _plugins.Clear();
        _diagnostics.Clear();

        var normalizedDirectories = (pluginDirectory ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var discovered = DiscoverByAssemblyScan(
            normalizedDirectories,
            hostVersion,
            _diagnostics);

        _plugins.AddRange(discovered);
    }

    private DiscoveredPlugin? DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        try
        {
            var plugin = _discoveredPluginFactory.BuildFromAssembly(assembly);
            if (!PluginCompatibilityEvaluator.IsCompatible(plugin.MinHostVersion, plugin.MaxHostVersion, hostVersion))
            {
                return null;
            }

            return plugin;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private IReadOnlyCollection<DiscoveredPlugin> DiscoverByAssemblyScan(
        IEnumerable<string> directories,
        Version hostVersion,
        ICollection<PluginLoadDiagnostic> diagnostics)
    {
        var plugins = new List<DiscoveredPlugin>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = _assembliesListFactory.BuildFromPaths(directories, diagnostics);

        foreach (var assembly in assemblies)
        {
            var plugin = DiscoverFromAssembly(assembly, hostVersion);
            if (plugin is null)
            {
                continue;
            }

            if (!seenIds.Add(plugin.Id))
            {
                continue;
            }

            plugins.Add(plugin);
        }

        return plugins;
    }
}
