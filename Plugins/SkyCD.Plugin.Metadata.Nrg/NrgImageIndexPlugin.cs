using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Nrg.Import;
using SkyCD.Plugin.Metadata.Nrg.Models;

namespace SkyCD.Plugin.Metadata.Nrg;

/// <summary>
/// Nero NRG disk-image metadata provider. When metadata providers are enabled, it contributes each
/// stored file as a work item materialized one at a time.
/// </summary>
public sealed class NrgImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly NrgIndexLimits limits;

    public NrgImageIndexPlugin() : this(NrgIndexLimits.Default)
    {
    }

    public NrgImageIndexPlugin(NrgIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".nrg", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return NrgIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
