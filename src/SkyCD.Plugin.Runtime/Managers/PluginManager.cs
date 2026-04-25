using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;

namespace SkyCD.Plugin.Runtime.Managers;

/// <summary>
/// Unified plugin manager for discovery, loading, and capability lookup.
/// </summary>
public sealed class PluginManager
{
    private readonly AssembliesListFactory _assembliesListFactory;
    private readonly DiscoveredPluginFactory _discoveredPluginFactory = new();
    private readonly List<DiscoveredPlugin> _plugins = [];
    private readonly ILogger<PluginManager> _logger;

    public IReadOnlyCollection<DiscoveredPlugin> Plugins => _plugins;

    public PluginManager()
        : this(NullLogger<PluginManager>.Instance, NullLogger.Instance)
    {
    }

    public PluginManager(
        ILogger<PluginManager> logger,
        ILogger assembliesLogger)
    {
        _logger = logger;
        _assembliesListFactory = new AssembliesListFactory(assembliesLogger);
    }

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

        var normalizedDirectories = (pluginDirectory ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var discovered = DiscoverByAssemblyScan(normalizedDirectories, hostVersion);

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
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Skipped assembly '{AssemblyName}' because it does not expose a compatible plugin type.", assembly.FullName);
            return null;
        }
    }

    private IReadOnlyCollection<DiscoveredPlugin> DiscoverByAssemblyScan(
        IEnumerable<string> directories,
        Version hostVersion)
    {
        var plugins = new List<DiscoveredPlugin>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = _assembliesListFactory.BuildFromPaths(directories);

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
