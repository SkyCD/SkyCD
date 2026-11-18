using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Presentation.MediaCache.Exceptions;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Host-side AI description surface over the durable media cache. Reading and clearing never require a
/// provider, so stored descriptions stay available offline and while the plugin is disabled. Generation
/// only ever runs after an explicit request.
/// </summary>
public sealed class AiDescriptionService
{
    /// <summary>Bounded parallelism for an explicit selected-item pass.</summary>
    public const int MaxBatchConcurrency = 2;

    private const string ContextFingerprintKey = "AiDescriptionContextFingerprint";

    private readonly CatalogDocumentRepository repository;
    private readonly IMediaCacheStore store;
    private readonly IGeneratedDescriptionCapability? capability;

    public AiDescriptionService(
        CatalogDocumentRepository repository,
        IMediaCacheStore store,
        IGeneratedDescriptionCapability? capability = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.capability = capability;
    }

    /// <summary>Reports the configured provider and the context an explicit pass would use.</summary>
    public AiDescriptionProviderInfo GetProviderInfo()
    {
        if (capability is null)
        {
            return new AiDescriptionProviderInfo(false, false, string.Empty, string.Empty, null, "unknown", null,
                "no AI description plugin is available");
        }

        if (capability is IDescriptionGenerationPlanner planner)
        {
            var plan = planner.GetPlan();
            return new AiDescriptionProviderInfo(true, plan.IsEnabled, plan.ProviderName, plan.ProviderVersion,
                plan.Model, plan.ProcessingDestination, plan.ContextOptions, plan.UnavailableReason);
        }

        return new AiDescriptionProviderInfo(true, true, capability.GetType().Name, string.Empty, null, "unknown",
            null, null);
    }

    /// <summary>
    /// Reads the stored description for an entry without starting generation or contacting a provider.
    /// Missing, stale and failed states are reported explicitly so the UI can show what is known offline.
    /// </summary>
    public AiDescriptionDetails Read(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var entry = repository.Get(entryId);
        if (entry is null)
        {
            return AiDescriptionDetails.Unavailable(entryId);
        }

        var records = store.GetRecordsForEntry(entryId)
            .Where(CatalogDescriptionService.IsGeneratedDescriptionRecord)
            .ToArray();
        if (records.Length == 0)
        {
            return AiDescriptionDetails.Unavailable(entryId);
        }

        var currentFingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, repository.GetAll());
        var accepted = records
            .Where(record => !string.IsNullOrWhiteSpace(CatalogDescriptionService.ReadDescriptionText(record)))
            .Where(record => record.State is MediaCacheRecordState.Captured
                or MediaCacheRecordState.Stale or MediaCacheRecordState.Missing)
            .OrderBy(record => record.State == MediaCacheRecordState.Captured ? 0 : 1)
            .ThenByDescending(record => record.CapturedAtUtc)
            .FirstOrDefault();

        if (accepted is not null)
        {
            var stale = accepted.State != MediaCacheRecordState.Captured ||
                        !string.Equals(accepted.Fingerprint, currentFingerprint, StringComparison.Ordinal);
            return Details(entryId, accepted, stale ? AiDescriptionState.Stale : AiDescriptionState.Current);
        }

        var failed = records
            .Where(record => record.State == MediaCacheRecordState.Failed)
            .OrderByDescending(record => record.CapturedAtUtc)
            .FirstOrDefault();
        return failed is null
            ? AiDescriptionDetails.Unavailable(entryId)
            : Details(entryId, failed, AiDescriptionState.Failed);
    }

    /// <summary>
    /// Runs one explicit generation request. Unless <paramref name="regenerate"/> is true, an entry whose
    /// source and context fingerprints still match its stored description is skipped without provider traffic.
    /// </summary>
    public async Task<AiDescriptionGenerationOutcome> GenerateAsync(
        string entryId,
        bool regenerate,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        cancellationToken.ThrowIfCancellationRequested();
        var provider = GetProviderInfo();
        if (!provider.IsPluginAvailable)
        {
            throw new AiDescriptionUnavailableException("no AI description plugin is available");
        }
        if (!provider.IsEnabled)
        {
            throw new AiDescriptionUnavailableException(provider.UnavailableReason ?? "generation is disabled");
        }

        var descriptions = new CatalogDescriptionService(repository, store);
        var options = provider.ContextOptions ?? new DescriptionContextOptions();
        var context = descriptions.Build(entryId, options);
        if (!regenerate && IsUnchanged(entryId, context))
        {
            return new AiDescriptionGenerationOutcome(Read(entryId), Skipped: true);
        }

        var description = await capability!
            .GenerateAsync(context, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        descriptions.Save(context, description);
        return new AiDescriptionGenerationOutcome(Read(entryId), Skipped: false);
    }

    /// <summary>
    /// Runs an explicit selected-item pass with bounded concurrency. Every entry settles with its own result;
    /// cancellation stops new work and marks unfinished entries as cancelled instead of failing the pass.
    /// </summary>
    public async Task<AiDescriptionBatchResult> GenerateBatchAsync(
        IReadOnlyList<string> entryIds,
        bool regenerate,
        IProgress<AiDescriptionBatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var ids = entryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var results = new AiDescriptionBatchItemResult[ids.Length];
        if (ids.Length == 0)
        {
            return new AiDescriptionBatchResult(results);
        }

        var provider = GetProviderInfo();
        if (!provider.IsPluginAvailable || !provider.IsEnabled)
        {
            throw new AiDescriptionUnavailableException(provider.UnavailableReason ??
                "no AI description plugin is available");
        }

        using var gate = new SemaphoreSlim(MaxBatchConcurrency, MaxBatchConcurrency);
        var completed = 0;
        await Task.WhenAll(ids.Select(async (entryId, index) =>
        {
            var acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
                var outcome = await GenerateAsync(entryId, regenerate, cancellationToken).ConfigureAwait(false);
                results[index] = new AiDescriptionBatchItemResult(entryId, ResolveName(entryId),
                    outcome.Skipped ? AiDescriptionBatchItemOutcome.Skipped : AiDescriptionBatchItemOutcome.Generated);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results[index] = new AiDescriptionBatchItemResult(entryId, ResolveName(entryId),
                    AiDescriptionBatchItemOutcome.Cancelled);
            }
            catch (Exception exception)
            {
                results[index] = new AiDescriptionBatchItemResult(entryId, ResolveName(entryId),
                    AiDescriptionBatchItemOutcome.Failed, exception.Message);
            }
            finally
            {
                if (acquired)
                {
                    gate.Release();
                }

                var finished = Interlocked.Increment(ref completed);
                progress?.Report(new AiDescriptionBatchProgress(finished, ids.Length, results[index]));
            }
        })).ConfigureAwait(false);

        return new AiDescriptionBatchResult(results);
    }

    /// <summary>Drops every stored description of the entry while leaving the catalog and comments untouched.</summary>
    public int Clear(string entryId) => new CatalogDescriptionService(repository, store).Clear(entryId);

    private bool IsUnchanged(string entryId, DescriptionContext context) =>
        store.GetRecordsForEntry(entryId)
            .Where(CatalogDescriptionService.IsGeneratedDescriptionRecord)
            .Any(record =>
                record.State == MediaCacheRecordState.Captured &&
                string.Equals(record.Fingerprint, context.SourceFingerprint, StringComparison.Ordinal) &&
                string.Equals(CatalogDescriptionService.ReadValue(record, ContextFingerprintKey),
                    context.ContextFingerprint, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(CatalogDescriptionService.ReadDescriptionText(record)));

    private string ResolveName(string entryId) => repository.Get(entryId)?.Name ?? entryId;

    private static AiDescriptionDetails Details(string entryId, MediaCacheRecord record, AiDescriptionState state) =>
        new(entryId,
            state,
            CatalogDescriptionService.ReadDescriptionText(record),
            CatalogDescriptionService.ReadValue(record, "AiDescriptionAgent"),
            CatalogDescriptionService.ReadValue(record, "AiDescriptionModel"),
            record.PluginVersion,
            CatalogDescriptionService.ReadValue(record, "AiDescriptionPromptVersion"),
            CatalogDescriptionService.ReadValue(record, "AiDescriptionLanguage"),
            CatalogDescriptionService.ReadValue(record, "AiDescriptionOrigin"),
            CatalogDescriptionService.ReadValue(record, "AiDescriptionProcessingDestination"),
            record.CapturedAtUtc,
            record.FailureReason);
}
