using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Lha.Import;
using SkyCD.Plugin.Metadata.Lha.Models;

namespace SkyCD.Plugin.Metadata.Lha;

/// <summary>
/// LHA archive metadata provider. When the host scans a path and metadata providers are enabled, it hooks
/// into the indexing sequence by contributing safely extracted entries as extra work items.
/// </summary>
public sealed class LhaArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly LhaIndexLimits limits;

    public LhaArchiveIndexPlugin() : this(LhaIndexLimits.Default)
    {
    }

    public LhaArchiveIndexPlugin(LhaIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsLhaFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return LhaIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsLhaFile(string fileName) =>
        fileName.EndsWith(".lha", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".lzh", StringComparison.OrdinalIgnoreCase);
}
