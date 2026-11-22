using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.XZ.Import;
using SkyCD.Plugin.Metadata.XZ.Models;

namespace SkyCD.Plugin.Metadata.XZ;

/// <summary>
/// XZ metadata provider. When the host scans a path and metadata providers are enabled, it hooks into the
/// indexing sequence by contributing the safely extracted stream as an extra work item.
/// </summary>
public sealed class XzArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly XzIndexLimits limits;

    public XzArchiveIndexPlugin() : this(XzIndexLimits.Default)
    {
    }

    public XzArchiveIndexPlugin(XzIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsXzFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return XzIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsXzFile(string fileName) =>
        fileName.EndsWith(".xz", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".txz", StringComparison.OrdinalIgnoreCase);
}
