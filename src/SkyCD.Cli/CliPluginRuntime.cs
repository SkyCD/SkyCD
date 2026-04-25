using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.Cli;

public sealed class CliPluginRuntime : IAsyncDisposable
{
    public required IReadOnlyList<DiscoveredPlugin> DiscoveredPlugins { get; init; }

    public required IReadOnlyList<string> PluginDirectories { get; init; }

    public required IServiceProvider ServiceProvider { get; init; }

    public static async Task<CliPluginRuntime> LoadAsync(Version hostVersion, CancellationToken cancellationToken = default)
    {
        var pluginDirectories = GetPluginDirectories();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.ColorBehavior = LoggerColorBehavior.Disabled;
                options.SingleLine = true;
                options.TimestampFormat = string.Empty;
            });
        });

        var pluginManager = new PluginManager(
            loggerFactory.CreateLogger<PluginManager>(),
            loggerFactory.CreateLogger("SkyCD.Plugin.Runtime.Factories.AssembliesListFactory"));
        pluginManager.Discover(string.Join(Path.PathSeparator, pluginDirectories), hostVersion);
        var pluginList = pluginManager.Plugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var serviceCollectionFactory = new ServiceCollectionFactory();
        var services = serviceCollectionFactory.BuildCommonServiceCollection();

        services.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        foreach (var plugin in pluginList)
        {
            var pluginServices = serviceCollectionFactory.BuildPluginServiceCollection(plugin);
            foreach (var descriptor in pluginServices)
            {
                services.Add(descriptor);
            }
        }

        var serviceProvider = services.BuildServiceProvider();
        PluginServiceProvider.Instance.Import(serviceProvider);

        var runtime = new CliPluginRuntime
        {
            DiscoveredPlugins = pluginList,
            PluginDirectories = pluginDirectories,
            ServiceProvider = serviceProvider
        };

        return runtime;
    }

    public ValueTask DisposeAsync()
    {
        PluginServiceProvider.Instance.Import(new ServiceCollection());
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
