using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>
/// Pure selection rules shared by the resolver and by callers that re-evaluate a snapshot against a
/// different host version or runtime. Compatibility precedence, highest first:
/// <list type="number">
/// <item>host version range (<see cref="PluginCompatibilityEvaluator"/>) - a host mismatch is incompatible;</item>
/// <item>platform list - the release must declare the running platform or no platform at all;</item>
/// <item>feed availability - a version the feed does not list is not installable;</item>
/// <item>prerelease exclusion - prereleases are compatible but never selected automatically.</item>
/// </list>
/// Availability is checked last because it needs the feed's version list; a release failing an earlier rule
/// is incompatible regardless of the feed.
/// </summary>
public static class PluginCatalogRules
{
    public static PluginCompatibilityReport Evaluate(PluginReleaseDefinition release, PluginCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(options);

        var host = PluginCompatibilityEvaluator.IsCompatible(release.MinHostVersion, release.MaxHostVersion,
            options.HostVersion);
        if (!host)
        {
            return release.MaxHostVersion is not null && options.HostVersion > release.MaxHostVersion
                ? PluginCompatibilityReport.HostTooNew(release.MaxHostVersion!, options.HostVersion)
                : PluginCompatibilityReport.HostTooOld(release.MinHostVersion, options.HostVersion);
        }

        if (!IsPlatformEligible(release.Platforms, options.RuntimeIdentifier))
        {
            return PluginCompatibilityReport.PlatformMismatch(
                string.Join(", ", release.Platforms), options.RuntimeIdentifier);
        }

        return PluginCompatibilityReport.Eligible();
    }

    /// <summary>
    /// Applies availability and prerelease rules on top of the static evaluation. Only a release that passed
    /// the host and platform rules can be unavailable or excluded as a prerelease; a release that is already
    /// incompatible keeps its specific reason. Returns <see langword="null"/> when the release is eligible.
    /// </summary>
    public static PluginCompatibilityReport? ApplyAvailability(PluginCompatibilityReport report,
        PluginReleaseDefinition release, Func<PluginPackageVersion, FeedAvailability> availability,
        PluginCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(options);

        if (!report.IsCompatible)
        {
            return null;
        }

        var state = availability(release.Version);
        if (state.State != FeedAvailabilityState.Available)
        {
            return state.State == FeedAvailabilityState.Failed
                ? PluginCompatibilityReport.Unknown(state.Reason)
                : PluginCompatibilityReport.NotPublished(state.Reason);
        }

        if (release.Version.IsPrerelease && !options.IncludePrerelease)
        {
            return PluginCompatibilityReport.PrereleaseExcluded();
        }

        return null;
    }

    /// <summary>Whether a release's platform list admits the running platform.</summary>
    public static bool IsPlatformEligible(IReadOnlyList<string> platforms, string runtimeIdentifier)
    {
        if (platforms.Count == 0)
        {
            return true;
        }

        var platform = PlatformOf(runtimeIdentifier);
        foreach (var candidate in platforms)
        {
            if (string.Equals(candidate, runtimeIdentifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(PlatformOf(candidate), platform, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The platform part of a runtime identifier such as <c>linux-musl-x64</c>.</summary>
    public static string PlatformOf(string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            return string.Empty;
        }

        var parts = runtimeIdentifier.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[0];
    }

    /// <summary>
    /// Re-evaluates every release of a package against the given options. Releases keep their order:
    /// newest first, then by whether they are eligible, then by version text so ties are deterministic.
    /// </summary>
    public static MarketplacePackage RebindPackage(MarketplacePackage package, PluginCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        var releases = package.Releases
            .Select(release => RebindRelease(release, options))
            .OrderBy(static release => release, Comparer<MarketplaceRelease>.Create(MarketplaceRelease.CompareBestFirst))
            .ThenBy(static release => release.IsEligible ? 0 : 1)
            .ThenBy(static release => release.Version.Original, StringComparer.Ordinal)
            .ToArray();

        var best = releases.FirstOrDefault(static release => release.IsEligible);
        return package with { Releases = releases, BestRelease = best };
    }

    public static MarketplaceRelease RebindRelease(MarketplaceRelease release, PluginCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(options);

        var definition = new PluginReleaseDefinition(release.Version, release.MinHostVersion,
            release.MaxHostVersion, release.Platforms);
        var report = release.Compatibility.Status == PluginCompatibilityStatus.Unknown
            ? release.Compatibility
            : Evaluate(definition, options);

        if (report.Status == PluginCompatibilityStatus.Unknown)
        {
            return release with { Compatibility = report };
        }

        var excluded = report.IsCompatible
            ? (release.Version.IsPrerelease && !options.IncludePrerelease
                ? PluginCompatibilityReport.PrereleaseExcluded()
                : null)
            : null;

        return release with
        {
            Compatibility = excluded ?? (release.Compatibility.Reason == PluginReleaseIncompatibility.NotPublished
                ? release.Compatibility
                : report)
        };
    }

    /// <summary>The single best release across every package ID, using deterministic tie-breaks.</summary>
    public static IReadOnlyList<MarketplaceRelease> SelectBestReleases(
        IEnumerable<MarketplacePackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        return packages
            .GroupBy(static package => package.Key, StringComparer.Ordinal)
            .Select(group => group.SelectMany(static package => package.Releases)
                .Where(static release => release.IsEligible)
                .OrderBy(static release => release,
                    Comparer<MarketplaceRelease>.Create(MarketplaceRelease.CompareBestFirst))
                .FirstOrDefault())
            .Where(static release => release is not null)
            .Select(static release => release!)
            .OrderBy(static release => release,
                Comparer<MarketplaceRelease>.Create(MarketplaceRelease.CompareBestFirst))
            .ToArray();
    }
}
