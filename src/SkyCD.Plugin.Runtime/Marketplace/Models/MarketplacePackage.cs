using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>One package advertised by one publisher manifest.</summary>
public sealed record MarketplacePackage
{
    /// <summary>Manifest URL that advertised the package.</summary>
    public required Uri SourceUri { get; init; }

    public required string PackageId { get; init; }

    public string? DisplayName { get; init; }

    public string? Summary { get; init; }

    public string? License { get; init; }

    public Uri? ProjectUrl { get; init; }

    public string? Publisher { get; init; }

    /// <summary>Icon key declared by the publisher manifest, or <see langword="null"/> when none is given.</summary>
    public string? Icon { get; init; }

    /// <summary>Feed declared by the manifest, or <see langword="null"/> when the feed could not be used.</summary>
    public Uri? FeedUri { get; init; }

    /// <summary>All releases, newest first, including incompatible, unavailable and prerelease ones.</summary>
    public IReadOnlyList<MarketplaceRelease> Releases { get; init; } = [];

    /// <summary>The highest eligible release, or <see langword="null"/> when nothing can be installed here.</summary>
    public MarketplaceRelease? BestRelease { get; init; }

    /// <summary>Stable key shared by duplicates of the same package ID across sources.</summary>
    public string Key => PackageId.ToLowerInvariant();

    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? PackageId : DisplayName!;

    /// <summary>Deterministic ordering: highest selected version, then package ID, then source URL.</summary>
    public static int CompareBestFirst(MarketplacePackage left, MarketplacePackage right)
    {
        var leftVersion = left.BestRelease?.Version;
        var rightVersion = right.BestRelease?.Version;

        if (leftVersion is not null && rightVersion is not null)
        {
            var comparison = rightVersion.CompareTo(leftVersion);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        else if (leftVersion is not null)
        {
            return -1;
        }
        else if (rightVersion is not null)
        {
            return 1;
        }

        var byId = string.CompareOrdinal(left.PackageId, right.PackageId);
        return byId != 0 ? byId : string.CompareOrdinal(left.SourceUri.AbsoluteUri, right.SourceUri.AbsoluteUri);
    }

    public override string ToString()
    {
        return $"{PackageId} ({Releases.Count} release(s)) from {SourceUri}";
    }
}
