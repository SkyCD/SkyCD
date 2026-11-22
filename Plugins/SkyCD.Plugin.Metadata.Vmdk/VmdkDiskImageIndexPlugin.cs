using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Vmdk.Import;
using SkyCD.Plugin.Metadata.Vmdk.Models;

namespace SkyCD.Plugin.Metadata.Vmdk;

/// <summary>
/// VMDK disk-image metadata provider. When the host has metadata providers enabled, it contributes
/// one contained file at a time to the indexing sequence.
/// </summary>
public sealed class VmdkDiskImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly VmdkIndexLimits limits;

    public VmdkDiskImageIndexPlugin() : this(VmdkIndexLimits.Default)
    {
    }

    public VmdkDiskImageIndexPlugin(VmdkIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsVmdkName(request.FileName) && !IsVmdkName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return VmdkIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsVmdkName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".vmdk", StringComparison.OrdinalIgnoreCase);
}
