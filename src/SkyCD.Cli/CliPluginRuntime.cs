using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Loading;

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
        var discoveryService = new PluginDirectoryDiscoveryService();
        var loadResult = discoveryService.Discover(pluginDirectories, new PluginLoadOptions
        {
            HostVersion = hostVersion,
            EnableAssemblyIsolation = false
        }, fallbackToAssemblyScan: true);

        var runtime = new CliPluginRuntime
        {
            DiscoveredPlugins = loadResult.Plugins.ToList(),
            Diagnostics = loadResult.Diagnostics
                .Select(diagnostic => $"{(diagnostic.IsError ? "error" : "info")}: {diagnostic.PluginId}: {diagnostic.Message}")
                .ToList(),
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
}
