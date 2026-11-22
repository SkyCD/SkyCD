using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Iso.Import;
using SkyCD.Plugin.Metadata.Iso.Models;

namespace SkyCD.Plugin.Metadata.Iso;

/// <summary>
/// ISO 9660 and UDF disk-image metadata provider. When the host has metadata providers enabled, it
/// hooks into the indexing sequence by contributing each stored file as a work item materialized one
/// at a time.
/// </summary>
public sealed class IsoImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly IsoIndexLimits limits;

    public IsoImageIndexPlugin() : this(IsoIndexLimits.Default)
    {
    }

    public IsoImageIndexPlugin(IsoIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".iso", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return IsoIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
