using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Cab.Import;
using SkyCD.Plugin.Metadata.Cab.Models;

namespace SkyCD.Plugin.Metadata.Cab;

/// <summary>
/// Microsoft Cabinet (CAB) archive metadata provider. When the host scans a path and metadata providers
/// are enabled, it hooks into the indexing sequence by contributing safely extracted entries as extra work
/// items.
/// </summary>
public sealed class CabArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly CabIndexLimits limits;

    public CabArchiveIndexPlugin() : this(CabIndexLimits.Default)
    {
    }

    public CabArchiveIndexPlugin(CabIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".cab", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return CabIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
