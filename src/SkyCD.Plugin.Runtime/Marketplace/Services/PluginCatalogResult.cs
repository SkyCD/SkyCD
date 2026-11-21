using System;
using System.Collections.Generic;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>The snapshot plus the details that explain it, for diagnostics and for issue #803 installers.</summary>
public sealed record PluginCatalogResult
{
    public required PluginCatalogSnapshot Snapshot { get; init; }

    /// <summary>Deduplicated, ordered explanations of invalid catalog entries, rejected manifests and
    /// malformed documents. Repeating the same refresh returns the same list in the same order.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Normalized, valid source URLs of the catalog, in catalog order.</summary>
    public IReadOnlyList<string> SourceUrls { get; init; } = [];

    /// <summary>An empty result for "nothing was configured", usable before the first resolution.</summary>
    public static PluginCatalogResult NotConfigured { get; } = new()
    {
        Snapshot = new PluginCatalogSnapshot
        {
            ResolvedAt = DateTimeOffset.MinValue,
            ExpiresAt = DateTimeOffset.MinValue,
            CatalogStatus = PluginCatalogStatus.NotConfigured,
            CatalogDetail = PluginSourceStatusDetail.ForCatalog(PluginCatalogStatus.NotConfigured)
        }
    };
}
