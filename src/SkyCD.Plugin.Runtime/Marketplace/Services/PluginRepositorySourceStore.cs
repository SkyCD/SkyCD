using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>
/// Repository URLs the user added on top of the built-in catalog. The catalog reads the current list on every
/// resolution, so a saved change applies without recreating the service. Only absolute HTTPS URLs without
/// credentials are kept, and a copy of a built-in catalog address is dropped: it is a catalog document, not a
/// publisher manifest, and the built-in catalog is already read first.
/// </summary>
public sealed class PluginRepositorySourceStore
{
    private readonly object gate = new();
    private IReadOnlyList<Uri> sources = [];

    /// <summary>The configured repository manifests, in the order the user entered them.</summary>
    public IReadOnlyList<Uri> Sources
    {
        get
        {
            lock (gate)
            {
                return sources;
            }
        }
    }

    /// <summary>Replaces the list; returns whether it actually changed.</summary>
    public bool SetSources(IEnumerable<Uri>? uris)
    {
        var next = (uris ?? [])
            .Where(PluginCatalogSourceDocuments.IsHttps)
            .Where(static uri => !PluginCatalogOptions.IsBuiltInCatalogEndpoint(uri))
            .DistinctBy(static uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (gate)
        {
            if (sources.SequenceEqual(next))
            {
                return false;
            }

            sources = next;
            return true;
        }
    }
}
