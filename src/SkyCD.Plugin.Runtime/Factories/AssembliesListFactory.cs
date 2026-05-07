using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace SkyCD.Plugin.Runtime.Factories;

public sealed class AssembliesListFactory(ILogger<AssembliesListFactory> logger)
{
    public IReadOnlyCollection<Assembly> BuildFromPaths(
        IEnumerable<string> directories)
    {
        var assemblies = new List<Assembly>();
        var seenAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Plugin directory not found: {Directory}", directory);
                continue;
            }

            var dllPaths = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(IsCandidatePluginAssemblyPath)
                .OrderBy(GetPriority)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var fullPath in dllPaths)
            {
                if (!seenAssemblyPaths.Add(fullPath))
                {
                    continue;
                }

                try
                {
                    var assemblyName = AssemblyName.GetAssemblyName(fullPath).Name;
                    if (string.IsNullOrWhiteSpace(assemblyName) || !seenAssemblyNames.Add(assemblyName))
                    {
                        continue;
                    }

                    var loadedAssembly = AppDomain.CurrentDomain
                        .GetAssemblies()
                        .FirstOrDefault(assembly =>
                            string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
                    if (loadedAssembly is not null)
                    {
                        assemblies.Add(loadedAssembly);
                        continue;
                    }

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

    private static bool IsCandidatePluginAssemblyPath(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.Equals(fileName, "SkyCD.Plugin.Abstractions.dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var refSegment = $"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}";
        var refIntSegment = $"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}";

        return !normalized.Contains(objSegment, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(refSegment, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(refIntSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPriority(string fullPath)
    {
        var normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var releaseSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}";
        var debugSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}";

        if (normalized.Contains(releaseSegment, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (normalized.Contains(debugSegment, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}
