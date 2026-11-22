using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Ace.Import;
using SkyCD.Plugin.Metadata.Ace.Models;

namespace SkyCD.Plugin.Metadata.Ace;

/// <summary>
/// ACE archive metadata provider. When the host scans a path and metadata providers are enabled, it reads
/// the checksummed ACE headers and contributes every stored (method 0) entry whose CRC-16 verifies as an
/// extra work item. Compressed ACE methods are listed but never decompressed, because the format is
/// proprietary and no license-compatible decompressor exists.
/// </summary>
public sealed class AceArchiveIndexPlugin : IIndexWorkItemProvider
{
    private readonly AceIndexLimits limits;

    public AceArchiveIndexPlugin() : this(AceIndexLimits.Default)
    {
    }

    public AceArchiveIndexPlugin(AceIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    /// <summary>Gets the typed diagnostics for entries the last call skipped while contributing work items.</summary>
    public IReadOnlyList<string> LastDiagnostics { get; private set; } = [];

    public async Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".ace", StringComparison.OrdinalIgnoreCase))
        {
            LastDiagnostics = [];
            return IndexWorkItemSet.Empty;
        }

        var result = await AceIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        LastDiagnostics = result.Skipped
            .Select(entry => string.IsNullOrEmpty(entry.Name)
                ? $"{entry.Code}: {entry.Reason}"
                : $"{entry.Code}: {entry.Name}: {entry.Reason}")
            .ToList();
        return result.WorkItems;
    }
}
