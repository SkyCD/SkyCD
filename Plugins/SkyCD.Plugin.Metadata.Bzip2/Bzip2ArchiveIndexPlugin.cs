using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Bzip2.Import;
using SkyCD.Plugin.Metadata.Bzip2.Models;

namespace SkyCD.Plugin.Metadata.Bzip2;

/// <summary>
/// BZIP2 metadata provider. When the host scans a path and metadata providers are enabled, it hooks into
/// the indexing sequence by contributing the safely extracted stream as an extra work item.
/// </summary>
public sealed class Bzip2ArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly Bzip2IndexLimits limits;

    public Bzip2ArchiveIndexPlugin() : this(Bzip2IndexLimits.Default)
    {
    }

    public Bzip2ArchiveIndexPlugin(Bzip2IndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsBzip2File(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return Bzip2IndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsBzip2File(string fileName)
    {
        foreach (var suffix in new[] { ".bz2", ".bz", ".tbz", ".tbz2" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
