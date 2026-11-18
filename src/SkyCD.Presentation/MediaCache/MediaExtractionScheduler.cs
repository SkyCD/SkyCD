using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Host-side scheduling of media extraction into the durable cache. One pass per entry runs the matching
/// plugin providers, converts their results into cache records and publishes them as a single batch, so a
/// cancelled or failed scan never leaves partial extraction results behind.
/// </summary>
/// <remarks>
/// Cached results are never written into catalog entry properties: captured metadata and previews live in
/// their own records, so refreshing extraction cannot overwrite user comments, tags or custom properties.
/// </remarks>
public sealed class MediaExtractionScheduler
{
    private const string ReasonTimeout = "timeout";
    private const string ReasonFailed = "failed";
    private const string ReasonArtifactLimit = "artifact-limit";

    private readonly MediaExtensionManager manager;
    private readonly IMediaCacheStore store;
    private readonly TimeProvider timeProvider;

    public MediaExtractionScheduler(
        MediaExtensionManager manager,
        IMediaCacheStore store,
        TimeProvider? timeProvider = null)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The cache this scheduler writes to.</summary>
    public IMediaCacheStore Store => store;

    /// <summary>
    /// Extracts and stores results for one entry. Current captured results are reused without running any
    /// plugin; a changed source fingerprint or provider identity marks the previous results stale instead of
    /// deleting them, so they stay readable while the media is disconnected.
    /// </summary>
    public async Task<MediaExtractionOutcome> ProcessAsync(
        MediaExtractionRequest request,
        IProgress<MediaProgress>? progress = null,
        CancellationToken cancellationToken = default,
        MediaExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EntryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Fingerprint);
        ArgumentNullException.ThrowIfNull(request.OpenSource);

        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var existing = store.GetRecordsForEntry(request.EntryId);
        var allIdentities = manager.ResolveProviders(request.Item);
        var identities = manager.ResolveProviders(request.Item, request.ProviderSelection);
        var identityKeys = allIdentities.Select(IdentityKey).ToHashSet(StringComparer.Ordinal);

        var ownsBatch = request.BatchId is null;
        var batchId = request.BatchId ?? store.BeginBatch().BatchId;

        try
        {
            if (allIdentities.Count == 0)
            {
                var marker = PlanUnsupportedMarker(request, existing, now);
                return StageAndPublish(request, batchId, ownsBatch, existing, identityKeys, now,
                    payloads: marker.Staged ? [new MediaCachePayload(marker.Record)] : [],
                    records: [marker.Record],
                    reusedCacheHit: !marker.Staged);
            }

            var reused = FindReusableRecords(request, existing, identities);
            if (!request.ForceRefresh && reused is not null)
            {
                foreach (var record in reused)
                {
                    store.Touch(record.Id, now);
                }

                return StageAndPublish(request, batchId, ownsBatch, existing, identityKeys, now,
                    payloads: [], records: reused, reusedCacheHit: true);
            }

            var results = await manager.ProcessAsync(request.Item, request.OpenSource, progress, cancellationToken,
                limits, request.ProviderSelection);

            // No usable result while captured data exists: a failure or a throwing provider must never
            // downgrade usable cached payloads.
            var captured = existing
                .Where(record =>
                    record.State == MediaCacheRecordState.Captured &&
                    string.Equals(record.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
                .ToArray();

            var producedNothing = results.Count == 0 || results.All(result => result.Failed);
            if (producedNothing && captured.Length > 0)
            {
                if (ownsBatch)
                {
                    store.DiscardBatch(batchId);
                }

                return new MediaExtractionOutcome
                {
                    EntryId = request.EntryId,
                    Fingerprint = request.Fingerprint,
                    SourceUnavailable = true,
                    BatchId = request.BatchId,
                    Records = captured,
                    PreservedRecordCount = captured.Length
                };
            }

            var plan = PlanFromResults(request, results, identities, existing, now);
            return StageAndPublish(request, batchId, ownsBatch, existing, identityKeys, now,
                payloads: plan.Payloads,
                records: plan.Payloads.Select(payload => payload.Record).ToArray(),
                preservedCount: plan.PreservedCount);
        }
        catch (Exception) when (ownsBatch)
        {
            store.DiscardBatch(batchId);
            throw;
        }
    }

    /// <summary>
    /// Marks every record of an entry that no currently registered provider produced as stale, without
    /// touching payloads. Used when a plugin is disabled or uninstalled and re-extraction is not possible.
    /// </summary>
    public IReadOnlyList<MediaCacheRecord> MarkSuperseded(string entryId, MediaItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(item);

        var identityKeys = manager.ResolveProviders(item).Select(IdentityKey).ToHashSet(StringComparer.Ordinal);
        var superseded = PlanSuperseded(
            store.GetRecordsForEntry(entryId),
            identityKeys,
            Array.Empty<string>(),
            timeProvider.GetUtcNow());

        foreach (var record in superseded)
        {
            store.Save(new MediaCachePayload(record));
        }

        return superseded;
    }

    private MediaExtractionOutcome StageAndPublish(
        MediaExtractionRequest request,
        string batchId,
        bool ownsBatch,
        IReadOnlyList<MediaCacheRecord> existing,
        IReadOnlyCollection<string> identityKeys,
        DateTimeOffset now,
        List<MediaCachePayload> payloads,
        IReadOnlyList<MediaCacheRecord> records,
        bool reusedCacheHit = false,
        int preservedCount = 0)
    {
        var writtenIds = payloads
            .Select(payload => payload.Record.Id)
            .Concat(records.Select(record => record.Id))
            .ToHashSet(StringComparer.Ordinal);

        var superseded = PlanSuperseded(existing, identityKeys, writtenIds, now, request.Fingerprint);
        foreach (var record in superseded)
        {
            payloads.Add(new MediaCachePayload(record));
        }

        foreach (var payload in payloads)
        {
            store.Stage(batchId, payload);
        }

        if (ownsBatch)
        {
            store.PublishBatch(batchId);
        }

        return new MediaExtractionOutcome
        {
            EntryId = request.EntryId,
            Fingerprint = request.Fingerprint,
            Reused = reusedCacheHit,
            Published = ownsBatch,
            BatchId = batchId,
            Records = records,
            SupersededRecords = superseded,
            PreservedRecordCount = preservedCount
        };
    }

    private IReadOnlyList<MediaCacheRecord>? FindReusableRecords(
        MediaExtractionRequest request,
        IReadOnlyList<MediaCacheRecord> existing,
        IReadOnlyList<MediaProviderIdentity> identities)
    {
        var reusable = new List<MediaCacheRecord>(identities.Count);

        foreach (var identity in identities)
        {
            var key = IdentityKey(identity);
            var record = existing.FirstOrDefault(candidate =>
                candidate.State == MediaCacheRecordState.Captured &&
                string.Equals(candidate.Fingerprint, request.Fingerprint, StringComparison.Ordinal) &&
                string.Equals(candidate.CreateIdentityKey(), key, StringComparison.Ordinal));

            if (record is null)
            {
                return null;
            }

            reusable.Add(record);
        }

        return reusable;
    }

    private static (MediaCacheRecord Record, bool Staged) PlanUnsupportedMarker(
        MediaExtractionRequest request,
        IReadOnlyList<MediaCacheRecord> existing,
        DateTimeOffset now)
    {
        var recordId = MediaCacheRecord.CreateUnsupportedId(request.Fingerprint);
        var current = existing.FirstOrDefault(record =>
            string.Equals(record.Id, recordId, StringComparison.Ordinal) &&
            string.Equals(record.Fingerprint, request.Fingerprint, StringComparison.Ordinal));

        if (current is not null && !request.ForceRefresh)
        {
            return (current, false);
        }

        return (new MediaCacheRecord
        {
            Id = recordId,
            EntryId = request.EntryId,
            Fingerprint = request.Fingerprint,
            PluginId = MediaCacheRecord.HostPluginId,
            PluginVersion = string.Empty,
            Capability = MediaCacheRecord.UnsupportedCapability,
            State = MediaCacheRecordState.Unsupported,
            CapturedAtUtc = now,
            LastUsedAtUtc = now,
            SourceLength = request.Item.Length
        }, true);
    }

    private ExtractionPlan PlanFromResults(
        MediaExtractionRequest request,
        IReadOnlyList<MediaProviderResult> results,
        IReadOnlyList<MediaProviderIdentity> identities,
        IReadOnlyList<MediaCacheRecord> existing,
        DateTimeOffset now)
    {
        var priorityByIdentity = identities.ToDictionary(IdentityKey, identity => identity.Priority);
        var capturedIdentities = existing
            .Where(record =>
                record.State == MediaCacheRecordState.Captured &&
                string.Equals(record.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
            .Select(record => record.CreateIdentityKey())
            .ToHashSet(StringComparer.Ordinal);

        var artifactLimit = store.Policy.MaxArtifactBytesPerRecord;
        var payloads = new List<MediaCachePayload>();
        var preserved = 0;

        foreach (var result in results)
        {
            // A failed result carries no schema, so its identity is completed from the registered provider
            // to keep one stable record per provider across failure and success.
            var provider = ResolveIdentity(result, identities);
            var schemaId = result.Schema?.Id ?? provider?.SchemaId;
            var schemaVersion = result.Schema?.Version ?? provider?.SchemaVersion ?? 0;
            var identityKey = MediaCacheRecord.CreateIdentityKey(
                result.PluginId, result.PluginVersion, result.Capability, schemaId, schemaVersion);

            var record = new MediaCacheRecord
            {
                Id = MediaCacheRecord.CreateId(
                    request.Fingerprint,
                    result.PluginId,
                    result.PluginVersion,
                    result.Capability,
                    schemaId,
                    schemaVersion),
                EntryId = request.EntryId,
                Fingerprint = request.Fingerprint,
                PluginId = result.PluginId,
                PluginVersion = result.PluginVersion,
                Capability = result.Capability,
                SchemaId = schemaId,
                SchemaVersion = schemaVersion,
                Priority = priorityByIdentity.TryGetValue(identityKey, out var priority) ? priority : 0,
                CapturedAtUtc = now,
                LastUsedAtUtc = now,
                SourceLength = request.Item.Length
            };

            if (result.Failed)
            {
                // A failed refresh must never destroy data that is still usable offline.
                if (capturedIdentities.Contains(identityKey))
                {
                    preserved++;
                    continue;
                }

                record.State = MediaCacheRecordState.Failed;
                record.TimedOut = result.TimedOut;
                record.FailureReason = result.TimedOut ? ReasonTimeout : ReasonFailed;
                record.SchemaFields = (provider?.SchemaFields ?? [])
                    .Select(MediaMetadataFieldRecord.From)
                    .ToList();
                payloads.Add(new MediaCachePayload(record));
                continue;
            }

            record.State = MediaCacheRecordState.Captured;
            record.Metadata = (result.Metadata?.Values ?? [])
                .Select(MediaMetadataValueRecord.From)
                .ToList();
            record.SchemaFields = (result.Schema?.Fields ?? [])
                .Select(MediaMetadataFieldRecord.From)
                .ToList();

            var artifacts = ReadArtifacts(result, record, artifactLimit);
            if (artifacts is null)
            {
                record.State = MediaCacheRecordState.Failed;
                record.FailureReason = ReasonArtifactLimit;
                payloads.Add(new MediaCachePayload(record));
                continue;
            }

            payloads.Add(new MediaCachePayload(record, artifacts));
        }

        return new ExtractionPlan(payloads, preserved);
    }

    /// <summary>Copies preview payloads into content-addressed references; null when the record is oversized.</summary>
    private static Dictionary<string, byte[]>? ReadArtifacts(
        MediaProviderResult result,
        MediaCacheRecord record,
        long artifactLimit)
    {
        var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (result.Preview is null)
        {
            return artifacts;
        }

        var index = 0;
        foreach (var artifact in result.Preview.Artifacts)
        {
            var data = artifact.Data.ToArray();
            if (record.ArtifactBytes + data.Length > artifactLimit)
            {
                record.Artifacts.Clear();
                record.ArtifactBytes = 0;
                return null;
            }

            var key = $"preview:{index++}";
            artifacts[key] = data;
            record.Artifacts.Add(new MediaArtifactReference
            {
                Key = key,
                ContentType = artifact.ContentType,
                Length = data.Length,
                Sha256 = MediaCacheHashing.Sha256Hex(data)
            });
            record.ArtifactBytes += data.Length;
        }

        return artifacts;
    }

    private static List<MediaCacheRecord> PlanSuperseded(
        IReadOnlyList<MediaCacheRecord> existing,
        IReadOnlyCollection<string> identityKeys,
        IReadOnlyCollection<string> writtenIds,
        DateTimeOffset now,
        string? currentFingerprint = null)
    {
        var superseded = new List<MediaCacheRecord>();

        foreach (var record in existing)
        {
            if (writtenIds.Contains(record.Id) || record.State == MediaCacheRecordState.Stale)
            {
                continue;
            }

            var isCurrent = (identityKeys.Contains(record.CreateIdentityKey()) || record.Capability == "generated-description") &&
                            (currentFingerprint is null ||
                             string.Equals(record.Fingerprint, currentFingerprint, StringComparison.Ordinal));

            if (isCurrent)
            {
                continue;
            }

            var clone = record.Clone();
            clone.State = MediaCacheRecordState.Stale;
            clone.LastUsedAtUtc = now;
            superseded.Add(clone);
        }

        return superseded;
    }

    private static string IdentityKey(MediaProviderIdentity identity) => MediaCacheRecord.CreateIdentityKey(
        identity.PluginId, identity.PluginVersion, identity.Capability, identity.SchemaId, identity.SchemaVersion);

    private static MediaProviderIdentity? ResolveIdentity(
        MediaProviderResult result,
        IReadOnlyList<MediaProviderIdentity> identities) => identities.FirstOrDefault(identity =>
        string.Equals(identity.PluginId, result.PluginId, StringComparison.Ordinal) &&
        string.Equals(identity.PluginVersion, result.PluginVersion, StringComparison.Ordinal) &&
        string.Equals(identity.Capability, result.Capability, StringComparison.Ordinal));

    private sealed record ExtractionPlan(List<MediaCachePayload> Payloads, int PreservedCount);
}
