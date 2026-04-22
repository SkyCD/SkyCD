using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Loading;
using System.Reflection;

namespace SkyCD.Cli;

public sealed class CliPluginRuntime : IAsyncDisposable
{
    private readonly List<IPlugin> plugins = [];

    public required IReadOnlyList<DiscoveredPlugin> DiscoveredPlugins { get; init; }

    public required IReadOnlyList<string> Diagnostics { get; init; }

    public required IReadOnlyList<string> PluginDirectories { get; init; }

    public static async Task<CliPluginRuntime> LoadAsync(Version hostVersion, CancellationToken cancellationToken = default)
    {
        var pluginDirectories = GetPluginDirectories();
        var loader = new PluginDirectoryLoader();
        var loadResult = loader.LoadFromDirectories(pluginDirectories, new PluginLoadOptions
        {
            HostVersion = hostVersion,
            EnableAssemblyIsolation = false
        });

        var discoveredPlugins = loadResult.Plugins.ToList();
        var diagnostics = loadResult.Diagnostics
            .Select(diagnostic => $"{(diagnostic.IsError ? "error" : "info")}: {diagnostic.PluginId}: {diagnostic.Message}")
            .ToList();

        if (discoveredPlugins.Count == 0)
        {
            diagnostics.Add("info: <fallback>: No manifest-based plugins loaded. Falling back to assembly scan.");
            discoveredPlugins.AddRange(DiscoverFromAssemblies(pluginDirectories, hostVersion, diagnostics));
        }

        var runtime = new CliPluginRuntime
        {
            DiscoveredPlugins = discoveredPlugins,
            Diagnostics = diagnostics,
            PluginDirectories = pluginDirectories
        };

        var context = new PluginLifecycleContext
        {
            HostVersion = hostVersion,
            Services = null
        };

        foreach (var discovered in runtime.DiscoveredPlugins)
        {
            runtime.plugins.Add(discovered.Plugin);
            await discovered.Plugin.OnLoadAsync(context, cancellationToken);
            await discovered.Plugin.OnInitializeAsync(context, cancellationToken);
            await discovered.Plugin.OnActivateAsync(context, cancellationToken);
        }

        return runtime;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var plugin in plugins)
        {
            await plugin.DisposeAsync();
        }
    }

    private static IReadOnlyList<string> GetPluginDirectories()
    {
        var candidates = new List<string>();
        AddConfiguredPluginDirectories(candidates);
        AddLocalPluginDirectories(candidates);

        return candidates
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddConfiguredPluginDirectories(ICollection<string> candidates)
    {
        var configured = Environment.GetEnvironmentVariable("SKYCD_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        foreach (var segment in configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.GetFullPath(segment));
        }
    }

    private static void AddLocalPluginDirectories(ICollection<string> candidates)
    {
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Plugins"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Plugins", "samples"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "Plugins"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "samples"));

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            candidates.Add(Path.Combine(current.FullName, "Plugins"));
            candidates.Add(Path.Combine(current.FullName, "Plugins", "samples"));
            current = current.Parent;
        }
    }

    private static IReadOnlyList<DiscoveredPlugin> DiscoverFromAssemblies(
        IReadOnlyList<string> pluginDirectories,
        Version hostVersion,
        ICollection<string> diagnostics)
    {
        var discoveryService = new PluginDiscoveryService();
        var discovered = new List<DiscoveredPlugin>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in pluginDirectories.Where(Directory.Exists))
        {
            foreach (var dllPath in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllPath);
                    var plugins = discoveryService.DiscoverFromAssembly(assembly, hostVersion);
                    foreach (var plugin in plugins)
                    {
                        if (!seenIds.Add(plugin.Plugin.Descriptor.Id))
                        {
                            continue;
                        }

                        discovered.Add(plugin);
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add($"info: <assembly-scan>: Skipped '{dllPath}': {exception.Message}");
                }
            }
        }

        return discovered;
    }
}
