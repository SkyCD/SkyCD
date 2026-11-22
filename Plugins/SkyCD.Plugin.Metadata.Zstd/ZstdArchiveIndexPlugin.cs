using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Zstd.Import;
using SkyCD.Plugin.Metadata.Zstd.Models;

namespace SkyCD.Plugin.Metadata.Zstd;

/// <summary>
/// Zstandard metadata provider. When the host scans a path and metadata providers are enabled, it hooks into
/// the indexing sequence by contributing the safely extracted stream as an extra work item.
/// </summary>
public sealed class ZstdArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly ZstdIndexLimits limits;

    public ZstdArchiveIndexPlugin() : this(ZstdIndexLimits.Default)
    {
    }

    public ZstdArchiveIndexPlugin(ZstdIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsZstdFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ZstdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsZstdFile(string fileName)
    {
        foreach (var suffix in new[] { ".zst", ".tzst" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
