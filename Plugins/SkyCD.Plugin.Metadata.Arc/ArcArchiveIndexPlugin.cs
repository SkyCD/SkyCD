using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Arc.Import;
using SkyCD.Plugin.Metadata.Arc.Models;

namespace SkyCD.Plugin.Metadata.Arc;

/// <summary>
/// ARC archive metadata provider. When the host scans a path and metadata providers are enabled, it hooks
/// into the indexing sequence by contributing safely extracted entries as extra work items. Entries whose
/// data cannot be decoded are skipped and reported through typed diagnostics.
/// </summary>
public sealed class ArcArchiveIndexPlugin : IIndexWorkItemProvider
{
    private const string ArcExtension = ".arc";
    private readonly ArcIndexLimits limits;

    public ArcArchiveIndexPlugin() : this(ArcIndexLimits.Default)
    {
    }

    public ArcArchiveIndexPlugin(ArcIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ArcExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ArcIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
