using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;

namespace SkyCD.Plugin.Runtime.Managers;

/// <summary>
/// Unified plugin manager for discovery, loading, and capability lookup.
/// </summary>
public sealed class PluginManager(
    ILogger<PluginManager> logger,
    AssembliesListFactory assembliesListFactory,
    DiscoveredPluginFactory discoveredPluginFactory,
    IRepository<PluginDocument> pluginRepository)
{
    private readonly DiscoveredPluginCollection plugins = [];
    private IReadOnlyCollection<DiscoveredPlugin> availablePlugins = [];

    public IReadOnlyCollection<DiscoveredPlugin> Plugins => plugins;

    /// <summary>Compatible discovered plugins, including disabled plugins for Options drafts.</summary>
    public IReadOnlyCollection<DiscoveredPlugin> AvailablePlugins => availablePlugins;

    public event EventHandler? PluginsChanged;

    /// <summary>Returns compatible discovered plugins, including disabled plugins, with the requested capability.</summary>
    public IReadOnlyList<string> GetAvailablePluginIds<TCapability>()
        where TCapability : class, IPluginCapability => availablePlugins
        .Where(static plugin => plugin.Capabilities.OfType<TCapability>().Any())
        .Select(static plugin => plugin.Id)
        .ToArray();

    public IReadOnlyList<TCapability> GetCapabilities<TCapability>()
        where TCapability : class, IPluginCapability
    {
        return plugins
            .SelectMany(plugin => plugin.Capabilities)
            .OfType<TCapability>()
            .ToList();
    }

    public void Discover(string? pluginDirectory, Version hostVersion)
    {
        var normalizedDirectories = (pluginDirectory ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var discovered = DiscoverByAssemblyScan(normalizedDirectories, hostVersion);

        availablePlugins = discovered;
        UpsertPluginDocuments(MapToPluginDocuments(discovered));
        var descriptors = GetPluginDescriptors();
        plugins.Import(discovered, descriptors);
        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<PluginDocument> GetPluginDescriptors()
    {
        return pluginRepository
            .GetAll()
            .Where(static mapped => !string.IsNullOrWhiteSpace(mapped.Id))
            .Select(static mapped =>
            {
                mapped.Constraints ??= new PluginConstraintsDocument();
                return mapped;
            })
            .ToList();
    }

    public void SavePluginEnabledStates(IEnumerable<(string PluginId, bool IsEnabled)> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var byId = GetPluginDescriptors()
            .ToDictionary(static descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (pluginId, isEnabled) in states)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                continue;
            }

            if (byId.TryGetValue(pluginId, out var descriptor))
            {
                descriptor.IsEnabled = isEnabled;
                continue;
            }

            byId[pluginId] = new PluginDocument
            {
                Id = pluginId,
                IsEnabled = isEnabled,
                IsAvailable = false
            };
        }

        foreach (var descriptor in byId.Values)
        {
            pluginRepository.Save(descriptor.Id, descriptor);
        }
    }

    private IReadOnlyCollection<PluginDocument> MapToPluginDocuments(IReadOnlyCollection<DiscoveredPlugin> discovered)
    {
        return new List<PluginDocument>(
            discovered.Select(plugin => plugin.ToDocument())
        );
    }

    private DiscoveredPlugin? DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        try
        {
            var plugin = discoveredPluginFactory.BuildFromAssembly(assembly);

            return !PluginCompatibilityEvaluator.IsCompatible(plugin.MinHostVersion, plugin.MaxHostVersion, hostVersion)
                ? null
                : plugin;
        }
        catch (Exception exception)
        {
            // Plugin binaries are untrusted: a stale assembly can throw binding errors (for example
            // MissingMethodException) while being inspected, and discovery must stay fail-safe.
            logger.LogWarning(exception,
                "Skipped assembly '{AssemblyName}' because it could not be loaded as a compatible plugin.",
                assembly.FullName);
            return null;
        }
    }

    private DiscoveredPluginCollection DiscoverByAssemblyScan(
        IEnumerable<string> directories,
        Version hostVersion)
    {
        var discovered = new DiscoveredPluginCollection();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = assembliesListFactory.BuildFromPaths(directories);

        foreach (var assembly in assemblies)
        {
            var plugin = DiscoverFromAssembly(assembly, hostVersion);
            if (plugin is null)
            {
                continue;
            }

            if (!seenIds.Add(plugin.Id))
            {
                continue;
            }

            discovered.Add(plugin);
        }

        return discovered;
    }

    private void UpsertPluginDocuments(IReadOnlyCollection<PluginDocument> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        var existingById = pluginRepository.GetAll()
            .Where(static descriptor => !string.IsNullOrWhiteSpace(descriptor.Id))
            .ToDictionary(static descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase);

        var discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in discovered)
        {
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                continue;
            }

            discoveredIds.Add(document.Id);
            if (existingById.TryGetValue(document.Id, out var existing))
            {
                document.IsEnabled = (!existing.IsEnabled && !existing.IsAvailable) ? true : existing.IsEnabled;
            }

            pluginRepository.Save(document.Id, document);
            existingById[document.Id] = document;
        }

        foreach (var existing in existingById.Values.Where(existing => !discoveredIds.Contains(existing.Id)))
        {
            existing.IsAvailable = false;
            pluginRepository.Save(existing.Id, existing);
        }
    }
}
