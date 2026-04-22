using System.Reflection;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// Shared plugin discovery flow used by both UI and CLI hosts.
/// </summary>
public sealed class PluginDirectoryDiscoveryService
{
    private readonly PluginDirectoryLoader loader = new();
    private readonly PluginDiscoveryService discoveryService = new();

    public PluginLoadResult Discover(IEnumerable<string> directories, PluginLoadOptions options, bool fallbackToAssemblyScan = true)
    {
        var normalizedDirectories = directories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = loader.LoadFromDirectories(normalizedDirectories, options);
        if (!fallbackToAssemblyScan || result.Plugins.Count > 0)
        {
            return result;
        }

        var diagnostics = result.Diagnostics.ToList();
        diagnostics.Add(new PluginLoadDiagnostic
        {
            PluginId = "<assembly-scan>",
            IsError = false,
            Message = "No manifest-based plugins loaded. Falling back to assembly scan."
        });

        var discovered = DiscoverByAssemblyScan(normalizedDirectories, options.HostVersion, diagnostics);
        return new PluginLoadResult
        {
            Plugins = discovered,
            Diagnostics = diagnostics
        };
    }

    private IReadOnlyCollection<DiscoveredPlugin> DiscoverByAssemblyScan(
        IEnumerable<string> directories,
        Version hostVersion,
        ICollection<PluginLoadDiagnostic> diagnostics)
    {
        var plugins = new List<DiscoveredPlugin>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var dllPath in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllPath);
                    var discovered = discoveryService.DiscoverFromAssembly(assembly, hostVersion);
                    foreach (var plugin in discovered)
                    {
                        if (!seenIds.Add(plugin.Plugin.Descriptor.Id))
                        {
                            continue;
                        }

                        plugins.Add(plugin);
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new PluginLoadDiagnostic
                    {
                        PluginId = "<assembly-scan>",
                        IsError = false,
                        Message = $"Skipped '{dllPath}': {exception.Message}"
                    });
                }
            }
        }

        return plugins;
    }
}
