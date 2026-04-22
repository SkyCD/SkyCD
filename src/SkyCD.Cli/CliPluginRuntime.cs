using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Loading;
using System.Text.Json;

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
        var configured = Environment.GetEnvironmentVariable("SKYCD_PLUGIN_PATH");
        var fromAppSettings = TryReadPluginPathFromAppSettings();
        return BuildPluginDirectories(configured, fromAppSettings);
    }

    internal static IReadOnlyList<string> BuildPluginDirectories(string? configuredPluginPaths, string? appSettingsPluginPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPluginPaths))
        {
            foreach (var segment in configuredPluginPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.GetFullPath(segment));
            }
        }

        if (!string.IsNullOrWhiteSpace(appSettingsPluginPath))
        {
            candidates.Add(Path.GetFullPath(appSettingsPluginPath));
        }

        return candidates
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? TryReadPluginPathFromAppSettings(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var optionsPath = Path.Combine(root, "SkyCD", "options.json");
        if (!File.Exists(optionsPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(optionsPath));
            if (!document.RootElement.TryGetProperty("PluginPath", out var pluginPathElement))
            {
                return null;
            }

            var pluginPath = pluginPathElement.GetString();
            return string.IsNullOrWhiteSpace(pluginPath) ? null : pluginPath.Trim();
        }
        catch
        {
            return null;
        }
    }
}
