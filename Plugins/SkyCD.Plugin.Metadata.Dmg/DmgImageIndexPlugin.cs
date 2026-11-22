using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Dmg.Import;
using SkyCD.Plugin.Metadata.Dmg.Models;

namespace SkyCD.Plugin.Metadata.Dmg;

/// <summary>
/// DMG (UDIF) disk-image metadata provider. When the host has metadata providers enabled, it
/// contributes one contained file at a time to the indexing sequence.
/// </summary>
public sealed class DmgImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly DmgIndexLimits limits;

    public DmgImageIndexPlugin() : this(DmgIndexLimits.Default)
    {
    }

    public DmgImageIndexPlugin(DmgIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsDmgName(request.FileName) && !IsDmgName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return DmgIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsDmgName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && string.Equals(Path.GetExtension(name), ".dmg", StringComparison.OrdinalIgnoreCase);
}
