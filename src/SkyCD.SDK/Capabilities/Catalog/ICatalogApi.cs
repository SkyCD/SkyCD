using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>
/// Read-only view of the currently open catalog, handed to plugins by the host. Plugins never receive the
/// host document type; they work against <see cref="CatalogEntry"/> projections.
/// </summary>
public interface ICatalogApi
{
    /// <summary>Gets the on-disk path of the open catalog, or null when no catalog is open.</summary>
    string? OpenedCatalogPath { get; }

    /// <summary>Gets every entry currently in the open catalog.</summary>
    Task<IReadOnlyList<CatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one entry by identifier, or null when it is no longer present.</summary>
    Task<CatalogEntry?> GetEntryAsync(string id, CancellationToken cancellationToken = default);
}
