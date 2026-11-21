using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>
/// The same package ID published by several sources. The group keeps the per-source entries so a browse UI
/// or an installer can attribute and choose the exact origin instead of collapsing the duplicates.
/// </summary>
public sealed record DuplicatePackageGroup(string PackageId, IReadOnlyList<MarketplacePackage> Packages)
{
    public MarketplacePackage? Winner =>
        Packages.OrderBy(static package => package,
                Comparer<MarketplacePackage>.Create(MarketplacePackage.CompareBestFirst))
            .FirstOrDefault();

    public override string ToString()
    {
        var origins = string.Join(", ", Packages.Select(static package => package.SourceUri.AbsoluteUri));
        return $"{PackageId} published by {origins}";
    }
}
