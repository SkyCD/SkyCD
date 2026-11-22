using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Zoo.Import;
using SkyCD.Plugin.Metadata.Zoo.Models;

namespace SkyCD.Plugin.Metadata.Zoo;

/// <summary>
/// ZOO archive metadata provider. When the host scans a path and metadata providers are enabled, it hooks
/// into the indexing sequence by contributing safely extracted entries as extra work items.
/// </summary>
public sealed class ZooArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly ZooIndexLimits limits;

    public ZooArchiveIndexPlugin() : this(ZooIndexLimits.Default)
    {
    }

    public ZooArchiveIndexPlugin(ZooIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".zoo", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ZooIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
