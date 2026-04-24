using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Loading;
using System.Text.Json;

namespace SkyCD.Cli;

public sealed class CliPluginRuntime : IAsyncDisposable
{
    public required IReadOnlyList<DiscoveredPlugin> DiscoveredPlugins { get; init; }

    public required IReadOnlyList<string> Diagnostics { get; init; }

    public required IReadOnlyList<string> PluginDirectories { get; init; }

    public required IServiceProvider ServiceProvider { get; init; }

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
            PluginDirectories = pluginDirectories,
            ServiceProvider = new PluginServiceProviderFactory().Build(loadResult.Plugins)
        };

        GlobalPluginServiceProvider.Set(runtime.ServiceProvider);

        return runtime;
    }

    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(GlobalPluginServiceProvider.Current, ServiceProvider))
        {
            GlobalPluginServiceProvider.Reset();
            return ValueTask.CompletedTask;
        }

        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return ValueTask.CompletedTask;
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
