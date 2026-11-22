using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Vhd.Import;
using SkyCD.Plugin.Metadata.Vhd.Models;

namespace SkyCD.Plugin.Metadata.Vhd;

/// <summary>
/// VHD and VHDX disk-image metadata provider. When the host has metadata providers enabled, it
/// contributes one contained file at a time to the indexing sequence.
/// </summary>
public sealed class VhdImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly VhdIndexLimits limits;

    public VhdImageIndexPlugin() : this(VhdIndexLimits.Default)
    {
    }

    public VhdImageIndexPlugin(VhdIndexLimits limits)
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

        return VhdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsImageName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (string.Equals(Path.GetExtension(name), ".vhd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(name), ".vhdx", StringComparison.OrdinalIgnoreCase));
}
