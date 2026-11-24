using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Import;

/// <summary>
/// Runs the enabled web sources in priority order and merges their facts so no single source can dominate.
/// Every source is isolated: a fetch or parse error for one is recorded as a failed result and the run continues
/// with the rest, and only cancellation or a missing importer interrupts the pass.
/// </summary>
public sealed class WebSourceOrchestrator
{
    private readonly Dictionary<string, IWebSourceImporter> importers;
    private readonly IWebFetcher fetcher;

    public WebSourceOrchestrator(IEnumerable<IWebSourceImporter> importers, IWebFetcher fetcher)
    {
        ArgumentNullException.ThrowIfNull(importers);
        ArgumentNullException.ThrowIfNull(fetcher);
        this.importers = importers.ToDictionary(importer => importer.SourceId, StringComparer.OrdinalIgnoreCase);
        this.fetcher = fetcher;
    }

    /// <summary>The source ids this orchestrator can actually serve, preserving the requested order.</summary>
    public IReadOnlyList<string> ResolveOrder(IReadOnlyList<string> requested) =>
        requested.Where(id => importers.ContainsKey(id)).ToArray();

    public async Task<ImportRun> RunAsync(
        ImportRequest request,
        IReadOnlyList<string> enabledSources,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(enabledSources);

        var merged = new DescriptionFacts();
        var results = new List<WebSourceResult>();

        foreach (var sourceId in enabledSources)
        {
            if (!importers.TryGetValue(sourceId, out var importer)) continue;

            string? url;
            try
            {
                url = importer.ResolveUrl(request);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(WebSourceResult.Failure(sourceId, null, $"{exception.GetType().Name}: {exception.Message}"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                results.Add(WebSourceResult.Failure(sourceId, null, "the source cannot serve this request"));
                continue;
            }

            try
            {
                var body = await fetcher.GetStringAsync(url, timeout, cancellationToken).ConfigureAwait(false);
                var sourceFacts = new DescriptionFacts();
                importer.Parse(body, request, sourceFacts);
                merged.AddSourceUrl(url);
                merged.AbsentFrom(sourceFacts);
                results.Add(WebSourceResult.Success(sourceId, url, sourceFacts));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(WebSourceResult.Failure(sourceId, url, $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        return new ImportRun(merged, results);
    }
}

/// <summary>The merged facts plus the per-source outcomes of one orchestrated import run.</summary>
public sealed record ImportRun(DescriptionFacts Facts, IReadOnlyList<WebSourceResult> Results);
