using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Runtime.Marketplace.Services;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>
/// Immutable result of a catalog resolution. The same snapshot is returned by <c>GetCatalogAsync</c> until
/// it expires or an explicit refresh replaces it, so callers can rebind UI to a new snapshot without
/// reasoning about concurrent mutation.
/// </summary>
public sealed record PluginCatalogSnapshot
{
    /// <summary>When this snapshot was produced.</summary>
    public required DateTimeOffset ResolvedAt { get; init; }

    /// <summary>The catalog endpoint that answered this time, or <see langword="null"/> when none could be read.</summary>
    public Uri? CatalogEndpoint { get; init; }

    /// <summary>State of the catalog listing document.</summary>
    public required PluginCatalogStatus CatalogStatus { get; init; }

    /// <summary>Explanation of <see cref="CatalogStatus"/>.</summary>
    public required PluginSourceStatusDetail CatalogDetail { get; init; }

    /// <summary>Number of fetches performed, including retries of the last known good cache; 0 means cached.</summary>
    public int Generation { get; init; }

    /// <summary>One entry per catalog source, in catalog order; invalid URLs appear as rejected entries.</summary>
    public IReadOnlyList<PluginSourceSnapshot> Sources { get; init; } = [];

    /// <summary>Packages from loaded sources, newest first; duplicated package IDs appear once per source.</summary>
    public IReadOnlyList<MarketplacePackage> Packages { get; init; } = [];

    /// <summary>The highest eligible release of every package ID across all sources, newest first.</summary>
    public IReadOnlyList<MarketplaceRelease> BestReleases { get; init; } = [];

    public IReadOnlyList<PluginSourceSnapshot> FailingSources =>
        Sources.Where(static source => source.IsFailing).ToArray();

    public IReadOnlyList<PluginSourceSnapshot> HealthySources =>
        Sources.Where(static source => source.Status == PluginSourceStatus.Loaded).ToArray();

    /// <summary>Catalog is empty and no source failed: nothing is published.</summary>
    public bool IsEmpty => Sources.Count == 0 && CatalogStatus != PluginCatalogStatus.Offline;

    /// <summary>No source failed because the network blocked the catalog resolution altogether.</summary>
    public bool IsOffline => CatalogStatus == PluginCatalogStatus.Offline;

    /// <summary>At least one source failed while another still has results.</summary>
    public bool HasPartialFailure => FailingSources.Count > 0 && HealthySources.Count > 0;

    /// <summary>Every source failed and no healthy package remains.</summary>
    public bool HasTotalFailure => FailingSources.Count > 0 && HealthySources.Count == 0;

    /// <summary>When <see cref="PluginCatalog.GetCatalogAsync"/> starts refreshing this snapshot again.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Packages of one source URL, in deterministic catalog order.</summary>
    public IReadOnlyList<MarketplacePackage> PackagesFrom(Uri manifestUri)
    {
        ArgumentNullException.ThrowIfNull(manifestUri);
        return Packages
            .Where(package => string.Equals(package.SourceUri.AbsoluteUri, manifestUri.AbsoluteUri,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>All packages with the given ID, one per source that publishes it.</summary>
    public IReadOnlyList<MarketplacePackage> FindPackages(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return Packages
            .Where(package => string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>The highest eligible release of the given package ID, or <see langword="null"/>.</summary>
    public MarketplaceRelease? FindBestRelease(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return BestReleases.FirstOrDefault(release =>
            string.Equals(release.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Duplicate packages across sources, grouped by package ID.</summary>
    public IReadOnlyList<DuplicatePackageGroup> Duplicates =>
        Packages
            .GroupBy(static package => package.Key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => new DuplicatePackageGroup(group.Key, group.ToArray()))
            .OrderBy(static group => group.PackageId, StringComparer.Ordinal)
            .ToArray();

    /// <summary>All package content URIs of the selected releases, for later download (#803).</summary>
    public IReadOnlyList<Uri> SelectedPackageContentUris =>
        BestReleases.Select(static release => release.PackageContentUri).OfType<Uri>().ToArray();

    /// <summary>One package by its exact source and ID, as an installer (#803) needs.</summary>
    public MarketplacePackage? FindPackage(Uri sourceUri, string packageId)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return Packages.FirstOrDefault(package =>
            string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.SourceUri.AbsoluteUri, sourceUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One release by source, ID and version, as an installer (#803) needs.</summary>
    public MarketplaceRelease? FindRelease(Uri sourceUri, string packageId, PluginPackageVersion version)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentNullException.ThrowIfNull(version);
        return FindPackage(sourceUri, packageId)?.Releases
            .FirstOrDefault(release => release.Version.Equals(version));
    }

    /// <summary>
    /// Re-evaluates packages against a different host version or runtime, for diagnostics only (for example
    /// "what would be selected on linux-arm64"). The snapshot itself is not modified.
    /// </summary>
    public static IReadOnlyList<MarketplacePackage> Rebind(
        IEnumerable<MarketplacePackage> packages, PluginCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(options);
        return packages
            .Select(package => PluginCatalogRules.RebindPackage(package, options))
            .OrderBy(static package => package, Comparer<MarketplacePackage>.Create(MarketplacePackage.CompareBestFirst))
            .ToArray();
    }
}
