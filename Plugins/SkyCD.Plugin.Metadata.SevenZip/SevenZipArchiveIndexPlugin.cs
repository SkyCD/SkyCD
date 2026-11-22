using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.SevenZip.Import;
using SkyCD.Plugin.Metadata.SevenZip.Models;

namespace SkyCD.Plugin.Metadata.SevenZip;

/// <summary>
/// 7z archive metadata provider. When the host scans a path and metadata providers are enabled, it hooks
/// into the indexing sequence by contributing safely extracted entries as extra work items.
/// </summary>
public sealed class SevenZipArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly SevenZipIndexLimits limits;

    public SevenZipArchiveIndexPlugin() : this(SevenZipIndexLimits.Default)
    {
    }

    public SevenZipArchiveIndexPlugin(SevenZipIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".7z", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return SevenZipIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
