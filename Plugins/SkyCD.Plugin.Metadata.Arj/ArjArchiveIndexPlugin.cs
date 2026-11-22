using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Arj.Import;
using SkyCD.Plugin.Metadata.Arj.Models;

namespace SkyCD.Plugin.Metadata.Arj;

/// <summary>
/// ARJ archive metadata provider. When the host scans a path and metadata providers are enabled, it hooks
/// into the indexing sequence by contributing safely extracted entries as extra work items.
/// </summary>
public sealed class ArjArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly ArjIndexLimits limits;

    public ArjArchiveIndexPlugin() : this(ArjIndexLimits.Default)
    {
    }

    public ArjArchiveIndexPlugin(ArjIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".arj", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ArjIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
