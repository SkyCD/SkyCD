using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Repositories;

namespace SkyCD.Plugin.Runtime.Managers;

/// <summary>
/// Unified plugin manager for discovery, loading, and capability lookup.
/// </summary>
public sealed class PluginManager(
    ILogger<PluginManager> logger,
    AssembliesListFactory assembliesListFactory,
    DiscoveredPluginFactory discoveredPluginFactory,
    PluginDocumentFactory pluginDocumentFactory,
    RepositoryManager repositoryManager)
{
    private readonly List<DiscoveredPlugin> plugins = [];

    public IReadOnlyCollection<DiscoveredPlugin> Plugins => plugins;

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
        plugins.Clear();

        var normalizedDirectories = (pluginDirectory ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var discovered = DiscoverByAssemblyScan(normalizedDirectories, hostVersion);

        var repository = GetPluginRepository();
        repository.UpsertPluginDocuments(MapToPluginDocuments(discovered));
        var descriptors = GetPluginDescriptors();
        var discoveredById = discovered.ToDictionary(static item => item.Plugin.Id, static item => item.Plugin, StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors.Where(static descriptor => descriptor.IsEnabled && descriptor.IsAvailable))
        {
            if (!discoveredById.TryGetValue(descriptor.Id, out var plugin))
            {
                continue;
            }

            plugins.Add(plugin);
        }
    }

    public IReadOnlyList<PluginDocument> GetPluginDescriptors()
    {
        return GetPluginRepository().GetAll();
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

        var repository = GetPluginRepository();
        foreach (var descriptor in byId.Values)
        {
            repository.Save(descriptor.Id, descriptor);
        }
    }

    private IReadOnlyCollection<PluginDocument> MapToPluginDocuments(IReadOnlyCollection<DiscoveredPluginSnapshot> discovered)
    {
        var now = DateTimeOffset.UtcNow;
        var documents = new List<PluginDocument>(discovered.Count);

        foreach (var snapshot in discovered)
        {
            documents.Add(pluginDocumentFactory.Create(snapshot.Plugin, snapshot.AssemblyPath, now));
        }

        return documents;
    }

    private DiscoveredPlugin? DiscoverFromAssembly(Assembly assembly, Version hostVersion)
    {
        try
        {
            var plugin = discoveredPluginFactory.BuildFromAssembly(assembly);
            if (!PluginCompatibilityEvaluator.IsCompatible(plugin.MinHostVersion, plugin.MaxHostVersion, hostVersion))
            {
                return null;
            }

            return plugin;
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Skipped assembly '{AssemblyName}' because it does not expose a compatible plugin type.", assembly.FullName);
            return null;
        }
    }

    private IReadOnlyCollection<DiscoveredPluginSnapshot> DiscoverByAssemblyScan(
        IEnumerable<string> directories,
        Version hostVersion)
    {
        var discovered = new List<DiscoveredPluginSnapshot>();
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

            discovered.Add(new DiscoveredPluginSnapshot(plugin, assembly.Location));
        }

        return discovered;
    }

    private PluginRepository GetPluginRepository()
    {
        var repository = repositoryManager.For<PluginDocument>();
        if (repository is PluginRepository typed)
        {
            return typed;
        }

        throw new InvalidOperationException("Repository for PluginDocument must be PluginRepository.");
    }

    private sealed record DiscoveredPluginSnapshot(DiscoveredPlugin Plugin, string AssemblyPath);
}
