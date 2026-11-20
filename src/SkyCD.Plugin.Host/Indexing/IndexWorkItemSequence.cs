using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Plugin.Host.Indexing;

/// <summary>One extra source that was indexed, with every provider result captured for it.</summary>
public sealed record IndexedWorkItem(string Name, long SizeBytes, DateTimeOffset? ModifiedUtc,
    IReadOnlyList<MediaProviderResult> Results);

/// <summary>
/// Runs the registered work-item providers for one source and then the existing metadata providers over
/// every item they contribute. Provider-owned item sets are disposed after their results are copied, so
/// temporary files never outlive indexing, including on failure or cancellation.
/// </summary>
public sealed class IndexWorkItemSequence
{
    private readonly MediaExtensionManager media;
    private readonly IIndexWorkItemProvider[] providers;

    public IndexWorkItemSequence(MediaExtensionManager media, IEnumerable<IIndexWorkItemProvider> providers)
    {
        this.media = media ?? throw new ArgumentNullException(nameof(media));
        this.providers = (providers ?? []).OrderBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Gets whether indexing has both a provider to expand sources and a metadata provider to run.</summary>
    public bool HasMetadataProviders => providers.Length > 0 && media.HasMetadataProviders;

    /// <summary>Indexes every item contributed for <paramref name="request"/>; contributes nothing when no provider matches.</summary>
    public async Task<IReadOnlyList<IndexedWorkItem>> ProcessAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!media.HasMetadataProviders)
        {
            return [];
        }

        var indexed = new List<IndexedWorkItem>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var set = await GetWorkItemsAsync(provider, request, cancellationToken).ConfigureAwait(false);
            if (set is null)
            {
                continue;
            }

            await using (set.ConfigureAwait(false))
            {
                foreach (var item in set.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var mediaItem = new MediaItem(Path.GetExtension(item.Name), null, item.SizeBytes);
                    if (media.ResolveProviders(mediaItem, MediaProviderSelection.StandardMetadata).Count == 0)
                    {
                        continue;
                    }

                    var results = await media.ProcessAsync(mediaItem, item.OpenSource, cancellationToken: cancellationToken,
                        selection: MediaProviderSelection.StandardMetadata).ConfigureAwait(false);
                    indexed.Add(new IndexedWorkItem(item.Name, item.SizeBytes, item.ModifiedUtc, results));
                }
            }
        }

        return indexed;
    }

    private static async Task<IndexWorkItemSet?> GetWorkItemsAsync(IIndexWorkItemProvider provider,
        IndexWorkItemRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetWorkItemsAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(provider.GetType(), "index", exception);
            return null;
        }
    }
}
