using System.Reflection;
using System.Runtime.Loader;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
///     Loads plugins from configured directories using plugin manifests.
/// </summary>
public sealed class PluginDirectoryLoader
{
    private readonly PluginDiscoveryService _discoveryService = new();
    private readonly PluginManifestReader _manifestReader = new();

    public PluginLoadResult LoadFromDirectories(IEnumerable<string> directories, PluginLoadOptions options)
    {
        var discoveredPlugins = new List<DiscoveredPlugin>();
        var diagnostics = new List<PluginLoadDiagnostic>();

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                diagnostics.Add(new PluginLoadDiagnostic
                {
                    PluginId = "<directory>",
                    IsError = false,
                    Message = $"Plugin directory not found: {directory}"
                });
                continue;
            }

            var manifests = Directory.GetFiles(directory, "plugin.json", SearchOption.AllDirectories);
            foreach (var manifestPath in manifests)
                TryLoadManifest(manifestPath, options, discoveredPlugins, diagnostics);
        }

        return new PluginLoadResult
        {
            Plugins = discoveredPlugins,
            Diagnostics = diagnostics
        };
    }

    private void TryLoadManifest(
        string manifestPath,
        PluginLoadOptions options,
        ICollection<DiscoveredPlugin> discoveredPlugins,
        ICollection<PluginLoadDiagnostic> diagnostics)
    {
        PluginManifest manifest;
        try
        {
            manifest = _manifestReader.ReadFromFile(manifestPath);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new PluginLoadDiagnostic
            {
                PluginId = "<manifest>",
                IsError = true,
                Message = $"Failed to read manifest '{manifestPath}': {exception.Message}"
            });
            return;
        }

        if (!Version.TryParse(manifest.MinHostVersion, out var minHostVersion))
        {
            diagnostics.Add(new PluginLoadDiagnostic
            {
                PluginId = manifest.Id,
                IsError = true,
                Message = $"Invalid MinHostVersion '{manifest.MinHostVersion}'."
            });
            return;
        }

        if (options.HostVersion < minHostVersion)
        {
            diagnostics.Add(new PluginLoadDiagnostic
            {
                PluginId = manifest.Id,
                IsError = false,
                Message =
                    $"Skipped incompatible plugin. Host version {options.HostVersion} < required {minHostVersion}."
            });
            return;
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Assembly));
        if (!File.Exists(assemblyPath))
        {
            diagnostics.Add(new PluginLoadDiagnostic
            {
                PluginId = manifest.Id,
                IsError = true,
                Message = $"Assembly not found: {assemblyPath}"
            });
            return;
        }

        try
        {
            var assembly = options.EnableAssemblyIsolation
                ? new PluginLoadContext().LoadFromAssemblyPath(assemblyPath)
                : Assembly.LoadFrom(assemblyPath);

            var discovered = _discoveryService.DiscoverFromAssembly(assembly, options.HostVersion);
            if (discovered.Count == 0)
            {
                diagnostics.Add(new PluginLoadDiagnostic
                {
                    PluginId = manifest.Id,
                    IsError = true,
                    Message = "No compatible plugin entrypoints were discovered."
                });
                return;
            }

            foreach (var plugin in discovered) discoveredPlugins.Add(plugin);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new PluginLoadDiagnostic
            {
                PluginId = manifest.Id,
                IsError = true,
                Message = $"Load failure: {exception.Message}"
            });
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext() : base(true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            return null;
        }
    }
}