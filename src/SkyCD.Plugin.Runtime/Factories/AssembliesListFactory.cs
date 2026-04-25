using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SkyCD.Plugin.Runtime.Factories;

internal sealed class AssembliesListFactory(ILogger logger)
{
    public IReadOnlyCollection<Assembly> BuildFromPaths(
        IEnumerable<string> directories)
    {
        var assemblies = new List<Assembly>();
        var seenAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Plugin directory not found: {Directory}", directory);
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
                    logger.LogWarning(exception, "Skipped '{AssemblyPath}' while scanning plugin assemblies.", fullPath);
                }
            }
        }

        return assemblies;
    }
}
