using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Mdf.Import;
using SkyCD.Plugin.Metadata.Mdf.Models;

namespace SkyCD.Plugin.Metadata.Mdf;

/// <summary>
/// Alcohol 120% disk-image metadata provider. An image is an MDS descriptor plus an MDF data file;
/// when the host has metadata providers enabled, each contained file is contributed as a work item
/// materialized one at a time.
/// </summary>
public sealed class MdfImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly MdfIndexLimits limits;

    public MdfImageIndexPlugin() : this(MdfIndexLimits.Default)
    {
    }

    public MdfImageIndexPlugin(MdfIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsMdfName(request.FileName) && !IsMdfName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return MdfIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsMdfName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (string.Equals(Path.GetExtension(name), ".mdf", StringComparison.OrdinalIgnoreCase)
         || string.Equals(Path.GetExtension(name), ".mds", StringComparison.OrdinalIgnoreCase));
}
