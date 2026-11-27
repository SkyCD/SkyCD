using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Runtime.Loading;
using SkyCD.SDK.Capabilities;

namespace SkyCD.Plugin.Runtime.Factories;

public sealed class AssembliesListFactory(ILogger<AssembliesListFactory> logger)
{
    /// <summary>Contexts created by the last <see cref="BuildFromPaths"/> call, one per plugin folder.</summary>
    public IReadOnlyCollection<PluginLoadContext> LoadedContexts { get; private set; } = [];

    public IReadOnlyCollection<Assembly> BuildFromPaths(
        IEnumerable<string> directories,
        CancellationToken cancellationToken = default)
    {
        var assemblies = new List<Assembly>();
        var contexts = new List<PluginLoadContext>();

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Plugin directory not found: {Directory}", directory);
                continue;
            }

            try
            {
                var resolver = CreateMetadataResolver(directory);
                foreach (var unreadable in PluginEntryScanner.FindUnreadableAssemblyPaths(directory, cancellationToken))
                {
                    logger.LogWarning("Skipped '{AssemblyPath}' while scanning plugin assemblies.",
                        unreadable);
                }

                BuildFromDirectory(directory, resolver, assemblies, contexts, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
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
        List<PluginLoadContext> contexts,
        CancellationToken cancellationToken)
    {
        // Same-named entries in DIFFERENT folders must all load (side-by-side versions); only
        // same-folder duplicates (case variants) are skipped.
        var seenNamesByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var contextsByFolder = new Dictionary<string, PluginLoadContext>(StringComparer.OrdinalIgnoreCase);

        foreach (var entryPath in PluginEntryScanner.FindEntryAssemblyPaths(directory, resolver, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = Path.GetDirectoryName(entryPath) ?? directory;
            if (!seenNamesByFolder.TryGetValue(folder, out var seenNames))
            {
                seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                seenNamesByFolder.Add(folder, seenNames);
            }

            var assemblyName = Path.GetFileNameWithoutExtension(entryPath);
            if (!seenNames.Add(assemblyName))
            {
                continue;
            }

            // A test host has already loaded this assembly into the default context. Reuse that
            // instance so static test seams stay attached to the types the host invokes. A second
            // copy in a collectible context would have its own statics and would hide injected
            // failures. Installed plugins are not already loaded, so they still get their own context.
            var alreadyLoaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            if (alreadyLoaded is not null)
            {
                assemblies.Add(alreadyLoaded);
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
