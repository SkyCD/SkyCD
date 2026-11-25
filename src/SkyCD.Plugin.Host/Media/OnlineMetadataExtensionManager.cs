using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

/// <summary>One online metadata source supplied by an enabled plugin.</summary>
public sealed record OnlineMetadataProvider(string PluginId, string PluginVersion,
    IOnlineMetadataSourceCapability Capability);

/// <summary>
/// Host facade over every enabled online metadata source. Description generators depend on this aggregate
/// instead of instantiating site parsers: it recognizes owned hosts, runs the requested sources in priority
/// order, and isolates each source so one broken site cannot stop the others.
/// </summary>
public sealed class OnlineMetadataExtensionManager : IOnlineMetadataService
{
    private readonly OnlineMetadataProvider[] providers;

    public OnlineMetadataExtensionManager(IEnumerable<OnlineMetadataProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers
            .Where(static provider => provider.Capability is not null)
            .Where(static provider => !string.IsNullOrWhiteSpace(provider.Capability.SourceId))
            .OrderBy(static provider => provider.PluginId, StringComparer.Ordinal)
            .ThenBy(static provider => provider.Capability.GetType().FullName, StringComparer.Ordinal)
            .DistinctBy(static provider => provider.Capability.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>True when at least one online metadata source is installed and enabled.</summary>
    public bool HasSources => providers.Length > 0;

    /// <summary>Source ids the host can serve right now, in a stable order.</summary>
    public IReadOnlyList<string> SourceIds =>
        providers.Select(static provider => provider.Capability.SourceId).ToArray();

    /// <summary>Known hosts of every available source, for recognizing a pasted source URL.</summary>
    public IReadOnlyList<string> KnownHosts => providers
        .SelectMany(static provider => provider.Capability.KnownHosts)
        .Where(static host => !string.IsNullOrWhiteSpace(host))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task<OnlineMetadataRun> FetchAsync(IReadOnlyList<string> sourceIds,
        OnlineMetadataRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<OnlineMetadataResult>();

        foreach (var sourceId in sourceIds)
        {
            var provider = providers.FirstOrDefault(candidate =>
                string.Equals(candidate.Capability.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
            if (provider is null) continue;

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                var result = await provider.Capability.FetchAsync(request, timeoutSource.Token)
                    .WaitAsync(timeoutSource.Token).ConfigureAwait(false);
                if (result is null)
                {
                    results.Add(OnlineMetadataResult.Failure(provider.Capability.SourceId, null,
                        "the source returned no result"));
                    continue;
                }

                results.Add(result);
                if (result.Succeeded) Merge(merged, result.Facts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "online-metadata", exception);
                results.Add(OnlineMetadataResult.Failure(provider.Capability.SourceId, null,
                    $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        return new OnlineMetadataRun(merged, results);
    }

    /// <summary>Copies facts under first-wins precedence so an earlier source is never overwritten.</summary>
    private static void Merge(Dictionary<string, string> merged, IReadOnlyDictionary<string, string> facts)
    {
        foreach (var (key, value) in facts)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) || merged.ContainsKey(key))
                continue;
            merged[key] = value;
        }
    }
}
