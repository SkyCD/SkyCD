using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Vdi.Import;
using SkyCD.Plugin.Metadata.Vdi.Models;

namespace SkyCD.Plugin.Metadata.Vdi;

/// <summary>
/// VirtualBox VDI disk-image metadata provider. When the host has metadata providers enabled, it
/// contributes one contained file at a time to the indexing sequence.
/// </summary>
public sealed class VdiImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly VdiIndexLimits limits;

    public VdiImageIndexPlugin() : this(VdiIndexLimits.Default)
    {
    }

    public VdiImageIndexPlugin(VdiIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsImageName(request.FileName) && !IsImageName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return VdiIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsImageName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".vdi", StringComparison.OrdinalIgnoreCase);
}
