using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Ccd.Import;
using SkyCD.Plugin.Metadata.Ccd.Models;

namespace SkyCD.Plugin.Metadata.Ccd;

/// <summary>
/// CloneCD disk-image metadata provider. A CloneCD image is a CCD descriptor plus an IMG data file;
/// when the host has metadata providers enabled, each contained file is contributed as a work item
/// materialized one at a time.
/// </summary>
public sealed class CcdImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly CcdIndexLimits limits;

    public CcdImageIndexPlugin() : this(CcdIndexLimits.Default)
    {
    }

    public CcdImageIndexPlugin(CcdIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsCcdName(request.FileName) && !IsCcdName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return CcdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsCcdName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        string.Equals(Path.GetExtension(name), ".ccd", StringComparison.OrdinalIgnoreCase);
}
