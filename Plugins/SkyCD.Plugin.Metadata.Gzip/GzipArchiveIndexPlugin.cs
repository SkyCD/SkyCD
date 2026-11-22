using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Gzip.Import;
using SkyCD.Plugin.Metadata.Gzip.Models;

namespace SkyCD.Plugin.Metadata.Gzip;

/// <summary>
/// GZIP metadata provider. When the host scans a path and metadata providers are enabled, it hooks into the
/// indexing sequence by contributing the safely extracted member as an extra work item.
/// </summary>
public sealed class GzipArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly GzipIndexLimits limits;

    public GzipArchiveIndexPlugin() : this(GzipIndexLimits.Default)
    {
    }

    public GzipArchiveIndexPlugin(GzipIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsGzipFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return GzipIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsGzipFile(string fileName) =>
        (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
         || fileName.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase))
        && !fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith(".tar.gzip", StringComparison.OrdinalIgnoreCase);
}
