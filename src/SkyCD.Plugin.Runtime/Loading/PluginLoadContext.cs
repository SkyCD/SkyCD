using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// One collectible load context per installed plugin folder (issue #1229). The plugin's entry
/// assembly and its private dependencies resolve inside the folder through an
/// <see cref="AssemblyDependencyResolver"/>; the shared host ABI (<c>SkyCD.SDK</c>) always comes
/// from the default context so a second copy is never loaded. Anything the folder does not carry
/// falls back to the default context (framework and already-loaded host assemblies).
/// </summary>
/// <remarks>
/// Contexts are collectible, but memory is reclaimed only once the host drops every instance the
/// context created. <see cref="Plugin.Runtime.Managers.PluginManager"/> unloads superseded contexts
/// on every discovery; installs, updates and removals still activate on the next host restart,
/// which the installer already stages and enforces.
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedHostAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkyCD.SDK",
    };

    private readonly AssemblyDependencyResolver resolver;

    public string PluginDirectory { get; }

    public PluginLoadContext(string pluginDirectory)
        : base($"SkyCD plugin: {pluginDirectory}", isCollectible: true)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new ArgumentException("Plugin directory must not be empty.", nameof(pluginDirectory));
        }

        PluginDirectory = Path.GetFullPath(pluginDirectory);
        resolver = new AssemblyDependencyResolver(PluginDirectory);
    }

    public static bool IsSharedHostAssembly(string? assemblyName) =>
        !string.IsNullOrWhiteSpace(assemblyName) && SharedHostAssemblies.Contains(assemblyName!);

    /// <summary>Loads the entry assembly of this plugin folder into this context.</summary>
    public Assembly LoadEntry(string entryAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        return LoadFromAssemblyPath(Path.GetFullPath(entryAssemblyPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && SharedHostAssemblies.Contains(name))
        {
            return null;
        }

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        if (path is not null)
        {
            return LoadFromAssemblyPath(path);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedAssemblyName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedAssemblyName);
        if (path is not null)
        {
            return LoadUnmanagedDllFromPath(path);
        }

        return base.LoadUnmanagedDll(unmanagedAssemblyName);
    }
}
