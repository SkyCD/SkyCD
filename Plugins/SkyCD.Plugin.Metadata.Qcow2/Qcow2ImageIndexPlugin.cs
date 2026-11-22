using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Qcow2.Import;
using SkyCD.Plugin.Metadata.Qcow2.Models;

namespace SkyCD.Plugin.Metadata.Qcow2;

/// <summary>
/// QCOW2 disk-image metadata provider. When the host has metadata providers enabled, it contributes
/// one contained file at a time to the indexing sequence.
/// </summary>
public sealed class Qcow2ImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly Qcow2IndexLimits limits;

    public Qcow2ImageIndexPlugin() : this(Qcow2IndexLimits.Default)
    {
    }

    public Qcow2ImageIndexPlugin(Qcow2IndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsQcow2Name(request.FileName) && !IsQcow2Name(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return Qcow2IndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsQcow2Name(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".qcow2", StringComparison.OrdinalIgnoreCase);
}
