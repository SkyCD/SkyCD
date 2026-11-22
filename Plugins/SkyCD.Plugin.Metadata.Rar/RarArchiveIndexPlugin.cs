using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Rar.Import;
using SkyCD.Plugin.Metadata.Rar.Models;

namespace SkyCD.Plugin.Metadata.Rar;

/// <summary>
/// RAR and RAR5 archive metadata provider. When the host scans a path and metadata providers are enabled,
/// it hooks into the indexing sequence by contributing safely extracted entries as extra work items.
/// </summary>
public sealed class RarArchiveIndexPlugin : IIndexWorkItemProvider
{
    private const string RarExtension = ".rar";

    private readonly RarIndexLimits limits;

    public RarArchiveIndexPlugin() : this(RarIndexLimits.Default)
    {
    }

    public RarArchiveIndexPlugin(RarIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), RarExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return RarIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
