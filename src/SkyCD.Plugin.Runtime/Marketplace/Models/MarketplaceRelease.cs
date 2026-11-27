using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>A package release that can be installed, bound to the source that published it, its feed and
/// package ID.</summary>
public sealed record MarketplaceRelease
{
    /// <summary>Manifest URL that advertised the release.</summary>
    public required Uri SourceUri { get; init; }

    /// <summary>NuGet feed the release is served from.</summary>
    public required Uri FeedUri { get; init; }

    /// <summary>NuGet package ID, exactly as the manifest writes it.</summary>
    public required string PackageId { get; init; }

    /// <summary>Package version as advertised by the manifest.</summary>
    public required PluginPackageVersion Version { get; init; }

    /// <summary>Minimum host version declared by the release.</summary>
    public required Version MinHostVersion { get; init; }

    /// <summary>Optional maximum host version declared by the release.</summary>
    public Version? MaxHostVersion { get; init; }

    /// <summary>Runtime identifiers the release is published for; empty means any platform.</summary>
    public IReadOnlyList<string> Platforms { get; init; } = [];

    public required PluginCompatibilityReport Compatibility { get; init; }

    /// <summary>Whether automatic selection may choose this release.</summary>
    public bool IsEligible => Compatibility.IsCompatible;

    public bool IsPrerelease => Version.IsPrerelease;

    /// <summary>The host range as shown to users, for example <c>3.0.0 - 3.9.99</c> or <c>3.0.0 or later</c>.</summary>
    public string HostRange =>
        MaxHostVersion is null
            ? $"{MinHostVersion} or later"
            : $"{MinHostVersion} - {MaxHostVersion}";

    /// <summary>The platforms as shown to users.</summary>
    public string PlatformsLabel => Platforms.Count == 0 ? "any platform" : string.Join(", ", Platforms);

    /// <summary>The <c>PackageBaseAddress/3.0.0</c> resource resolved from the feed's service index, or
    /// <see langword="null"/> when the feed could not be read or exposes none.</summary>
    public Uri? PackageContentBaseUri { get; init; }

    /// <summary>The exact package URL from NuGet registration metadata, when available.</summary>
    public Uri? RegisteredPackageContentUri { get; init; }

    /// <summary>The package content URL (<c>.nupkg</c>) the NuGet v3 package content resource serves for this
    /// release, or <see langword="null"/> when the feed exposes no package content resource.</summary>
    public Uri? PackageContentUri
    {
        get
        {
            if (RegisteredPackageContentUri is not null)
            {
                return RegisteredPackageContentUri;
            }

            if (PackageContentBaseUri is null)
            {
                return null;
            }

            var baseAddress = PackageContentBaseUri.AbsoluteUri.EndsWith('/')
                ? PackageContentBaseUri
                : new Uri(PackageContentBaseUri.AbsoluteUri + "/");
            return new Uri(baseAddress,
                $"{PackageId.ToLowerInvariant()}/{Version.Normalized.ToLowerInvariant()}/{PackageId.ToLowerInvariant()}." +
                $"{Version.Normalized.ToLowerInvariant()}.nupkg");
        }
    }

    /// <summary>Deterministic ordering: highest version first, then source URL, package ID and feed ordinal.</summary>
    public static int CompareBestFirst(MarketplaceRelease left, MarketplaceRelease right)
    {
        var comparison = right.Version.CompareTo(left.Version);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.SourceUri.AbsoluteUri, right.SourceUri.AbsoluteUri);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.PackageId, right.PackageId);
        return comparison != 0 ? comparison : string.CompareOrdinal(left.FeedUri.AbsoluteUri, right.FeedUri.AbsoluteUri);
    }

    public override string ToString()
    {
        return $"{PackageId} {Version.Normalized} from {SourceUri}";
    }
}
