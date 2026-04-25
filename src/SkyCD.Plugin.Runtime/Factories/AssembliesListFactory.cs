using System.Reflection;
using SkyCD.Plugin.Runtime.Loading;

namespace SkyCD.Plugin.Runtime.Factories;

internal sealed class AssembliesListFactory
{
    public IReadOnlyCollection<Assembly> BuildFromPaths(
        IEnumerable<string> directories,
        ICollection<PluginLoadDiagnostic> diagnostics)
    {
        var assemblies = new List<Assembly>();
        var seenAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            foreach (var dllPath in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(dllPath);
                if (!seenAssemblyPaths.Add(fullPath))
                {
                    continue;
                }

                try
                {
                    assemblies.Add(Assembly.LoadFrom(fullPath));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new PluginLoadDiagnostic
                    {
                        PluginId = "<assembly-scan>",
                        IsError = false,
                        Message = $"Skipped '{fullPath}': {exception.Message}"
                    });
                }
            }
        }

        return assemblies;
    }
}
