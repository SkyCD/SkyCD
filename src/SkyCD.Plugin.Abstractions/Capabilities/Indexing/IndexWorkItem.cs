using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Indexing;

/// <summary>
/// One extra source a plugin adds to the host indexing sequence. The open factory must return a new
/// readable stream; the host owns and disposes every stream it opens.
/// </summary>
public sealed record IndexWorkItem(
    string Name,
    long SizeBytes,
    Func<CancellationToken, Task<Stream>> OpenSource,
    DateTimeOffset? ModifiedUtc = null);

/// <summary>
/// The extra sources one provider contributes for a single input. Disposing the set releases every file
/// or temporary directory the provider created for those sources and never throws.
/// </summary>
public sealed class IndexWorkItemSet : IAsyncDisposable
{
    private readonly Func<ValueTask>? cleanup;

    /// <summary>
    /// Initializes a set with the contributed items and an optional best-effort cleanup callback.
    /// </summary>
    public IndexWorkItemSet(IReadOnlyList<IndexWorkItem> items, Func<ValueTask>? cleanup = null)
    {
        Items = items ?? [];
        this.cleanup = cleanup;
    }

    /// <summary>A set that contributes nothing and owns no resources.</summary>
    public static IndexWorkItemSet Empty { get; } = new([]);

    /// <summary>Gets the items the host should index.</summary>
    public IReadOnlyList<IndexWorkItem> Items { get; }

    /// <summary>Releases provider-owned resources; cleanup failures are ignored.</summary>
    public async ValueTask DisposeAsync()
    {
        if (cleanup is null)
        {
            return;
        }

        try
        {
            await cleanup().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cleanup is best-effort; indexing results are already in host-owned memory.
        }
    }
}
