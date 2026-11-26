using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Runtime.Loading;
using SkyCD.SDK.Capabilities;

namespace SkyCD.Plugin.Runtime.Factories;

public sealed class AssembliesListFactory(ILogger<AssembliesListFactory> logger)
{
    /// <summary>Contexts created by the last <see cref="BuildFromPaths"/> call, one per plugin folder.</summary>
    public IReadOnlyCollection<PluginLoadContext> LoadedContexts { get; private set; } = [];

    public IReadOnlyCollection<Assembly> BuildFromPaths(
        IEnumerable<string> directories)
    {
        var assemblies = new List<Assembly>();
        var contexts = new List<PluginLoadContext>();

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Plugin directory not found: {Directory}", directory);
                continue;
            }

            try
            {
                var resolver = CreateMetadataResolver(directory);
                foreach (var unreadable in PluginEntryScanner.FindUnreadableAssemblyPaths(directory))
                {
                    logger.LogWarning("Skipped '{AssemblyPath}' while scanning plugin assemblies.",
                        unreadable);
                }

                BuildFromDirectory(directory, resolver, assemblies, contexts);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Skipped plugin directory '{Directory}' while scanning assemblies.",
                    directory);
            }
        }

        LoadedContexts = contexts;
        return assemblies;
    }

    private void BuildFromDirectory(
        string directory,
        PathAssemblyResolver resolver,
        List<Assembly> assemblies,
        List<PluginLoadContext> contexts)
    {
        // Same-named entries in DIFFERENT folders must all load (side-by-side versions); only
        // same-folder duplicates (case variants) are skipped.
        var seenNamesByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var contextsByFolder = new Dictionary<string, PluginLoadContext>(StringComparer.OrdinalIgnoreCase);

        foreach (var entryPath in PluginEntryScanner.FindEntryAssemblyPaths(directory, resolver))
        {
            var folder = Path.GetDirectoryName(entryPath) ?? directory;
            if (!seenNamesByFolder.TryGetValue(folder, out var seenNames))
            {
                seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                seenNamesByFolder.Add(folder, seenNames);
            }

            if (!seenNames.Add(Path.GetFileNameWithoutExtension(entryPath)))
            {
                continue;
            }

            if (!contextsByFolder.TryGetValue(folder, out var context))
            {
                context = new PluginLoadContext(folder);
                contextsByFolder.Add(folder, context);
                contexts.Add(context);
            }

            try
            {
                assemblies.Add(context.LoadEntry(entryPath));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Skipped '{AssemblyPath}' while scanning plugin assemblies.",
                    entryPath);
            }
        }
    }

    private static PathAssemblyResolver CreateMetadataResolver(string directory)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (var candidate in trustedAssemblies.Split(Path.PathSeparator))
            {
                AddIfFile(paths, candidate);
            }
        }

        AddIfFile(paths, typeof(object).Assembly.Location);
        AddIfFile(paths, typeof(IPluginCapability).Assembly.Location);
        // Nested plugin folders carry their own references. MetadataLoadContext only resolves
        // paths listed here, so a top-level-only set hides every capability below the root.
        foreach (var candidate in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            AddIfFile(paths, Path.GetFullPath(candidate));
        }

        return new PathAssemblyResolver(paths);
    }

    private static void AddIfFile(HashSet<string> paths, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                paths.Add(Path.GetFullPath(candidate));
            }
            catch (Exception)
            {
            }
        }
    }
}
