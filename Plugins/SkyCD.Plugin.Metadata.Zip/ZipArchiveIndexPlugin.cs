using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Zip.Import;
using SkyCD.Plugin.Metadata.Zip.Models;

namespace SkyCD.Plugin.Metadata.Zip;

/// <summary>
/// ZIP archive metadata provider. When the host scans a path and metadata providers are enabled, it hooks
/// into the indexing sequence by contributing safely extracted entries as extra work items.
/// </summary>
public sealed class ZipArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly ZipIndexLimits limits;

    public ZipArchiveIndexPlugin() : this(ZipIndexLimits.Default)
    {
    }

    public ZipArchiveIndexPlugin(ZipIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ZipIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }
}
