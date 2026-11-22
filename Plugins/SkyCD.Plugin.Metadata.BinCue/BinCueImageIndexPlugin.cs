using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.BinCue.Import;
using SkyCD.Plugin.Metadata.BinCue.Models;

namespace SkyCD.Plugin.Metadata.BinCue;

/// <summary>
/// BIN/CUE disk-image metadata provider. A BIN/CUE image is a CUE text descriptor plus the raw BIN
/// track data; when the host has metadata providers enabled, each contained file is contributed as a
/// work item materialized one at a time.
/// </summary>
public sealed class BinCueImageIndexPlugin : IIndexWorkItemProvider
{
    private readonly BinCueIndexLimits limits;

    public BinCueImageIndexPlugin() : this(BinCueIndexLimits.Default)
    {
    }

    public BinCueImageIndexPlugin(BinCueIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsCueName(request.FileName) && !IsCueName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return BinCueIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsCueName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        string.Equals(Path.GetExtension(name), ".cue", StringComparison.OrdinalIgnoreCase);
}
