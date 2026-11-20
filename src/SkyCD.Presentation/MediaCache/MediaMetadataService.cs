using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Presentation.MediaCache;

public sealed class MediaMetadataService
{
    public const string SourcePathPropertyKey = "FullName";

    private const int FileBufferBytes = 128 * 1024;
    private readonly MediaExtensionManager manager;
    private readonly MediaExtractionScheduler scheduler;
    private readonly IndexWorkItemSequence? workItems;
    private readonly TimeProvider timeProvider;

    public MediaMetadataService(MediaExtensionManager manager, IMediaCacheStore store, TimeProvider? timeProvider = null,
        IndexWorkItemSequence? workItems = null)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        scheduler = new MediaExtractionScheduler(manager, store, timeProvider);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.workItems = workItems;
    }

    public bool CanCollect => manager.HasMetadataProviders;

    public async Task<MediaMetadataOutcome> CollectAsync(
        CatalogDocument entry,
        IReadOnlyList<CatalogDocument> catalog,
        IProgress<MediaProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? batchId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(catalog);

        var path = ReadString(entry.Properties, SourcePathPropertyKey);
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.NoSourcePath);
        }

        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
            {
                return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceUnavailable);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceUnavailable);
        }

        if (file.Length != entry.Size || !MatchesRecordedTimestamp(entry, file))
        {
            return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceChanged);
        }

        var stored = false;
        MediaMetadataOutcome? failure = null;
        foreach (var selection in new[] { MediaProviderSelection.StandardMetadata, MediaProviderSelection.OptInMetadata })
        {
            var request = MediaExtractionRequest.ForCatalogEntry(
                entry, token => OpenReadAsync(path, token), catalog, batchId: batchId, providerSelection: selection);
            if (manager.ResolveProviders(request.Item, selection).Count == 0)
            {
                continue;
            }

            var limits = selection == MediaProviderSelection.OptInMetadata
                ? new MediaExecutionLimits(SourceBytes: Math.Max(file.Length, 1), Concurrency: 1,
                    TimeoutMilliseconds: int.MaxValue)
                : null;
            var outcome = await scheduler.ProcessAsync(request, progress, cancellationToken, limits).ConfigureAwait(false);
            if (outcome.SourceUnavailable)
            {
                failure ??= MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceUnavailable);
                continue;
            }

            var failed = outcome.Records.FirstOrDefault(record => record.State == MediaCacheRecordState.Failed);
            if (failed is not null)
            {
                failure ??= new MediaMetadataOutcome(entry.Id, Stored: false, MediaMetadataSkipReason.Failed, failed.FailureReason);
                continue;
            }

            stored = true;
        }

        await IndexWorkItemMetadataAsync(entry, path, file, catalog, batchId, cancellationToken).ConfigureAwait(false);

        return failure ?? (stored
            ? new MediaMetadataOutcome(entry.Id, Stored: true, MediaMetadataSkipReason.None, FailureReason: null)
            : MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.NoProvider));
    }

    /// <summary>
    /// Runs plugin-contributed work items for the source and stores their metadata on the owning entry.
    /// Item results use an item-scoped fingerprint so several contained files keep separate records.
    /// </summary>
    private async Task IndexWorkItemMetadataAsync(
        CatalogDocument entry,
        string path,
        FileInfo file,
        IReadOnlyList<CatalogDocument> catalog,
        string? batchId,
        CancellationToken cancellationToken)
    {
        if (workItems is not { HasMetadataProviders: true } sequence)
        {
            return;
        }

        var indexed = await sequence.ProcessAsync(new IndexWorkItemRequest
        {
            FileName = Path.GetFileName(path),
            SizeBytes = file.Length,
            OpenSource = token => OpenReadAsync(path, token)
        }, cancellationToken).ConfigureAwait(false);

        if (indexed.Count == 0)
        {
            return;
        }

        var store = scheduler.Store;
        var ownsBatch = batchId is null;
        var effectiveBatch = batchId ?? store.BeginBatch().BatchId;
        try
        {
            var now = timeProvider.GetUtcNow();
            var entryFingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, catalog);
            foreach (var item in indexed)
            {
                var fingerprint = MediaSourceFingerprint.Create(
                    entryFingerprint, item.Name, item.SizeBytes, item.ModifiedUtc);
                var priority = item.Results.Count;
                foreach (var result in item.Results)
                {
                    priority--;
                    if (result.Failed || result.Metadata is null)
                    {
                        continue;
                    }

                    var schema = result.Schema;
                    var record = new MediaCacheRecord
                    {
                        Id = MediaCacheRecord.CreateId(fingerprint, result.PluginId, result.PluginVersion,
                            result.Capability, schema?.Id, schema?.Version ?? 0),
                        EntryId = entry.Id,
                        Fingerprint = fingerprint,
                        PluginId = result.PluginId,
                        PluginVersion = result.PluginVersion,
                        Capability = result.Capability,
                        SchemaId = schema?.Id,
                        SchemaVersion = schema?.Version ?? 0,
                        Priority = priority,
                        State = MediaCacheRecordState.Captured,
                        CapturedAtUtc = now,
                        LastUsedAtUtc = now,
                        SourceLength = item.SizeBytes,
                        Metadata = result.Metadata.Values.Select(MediaMetadataValueRecord.From).ToList(),
                        SchemaFields = (schema?.Fields ?? []).Select(MediaMetadataFieldRecord.From).ToList()
                    };
                    record.MetadataBytes = record.CalculateMetadataBytes();
                    store.Stage(effectiveBatch, new MediaCachePayload(record));
                }
            }

            if (ownsBatch)
            {
                store.PublishBatch(effectiveBatch);
            }
        }
        catch
        {
            if (ownsBatch)
            {
                store.DiscardBatch(effectiveBatch);
            }

            throw;
        }
    }

    private static Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    internal static bool MatchesRecordedTimestamp(CatalogDocument entry, FileInfo file)
    {
        if (!entry.Properties.TryGetValue("LastWriteTime", out var raw) || raw is null)
        {
            return true;
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recorded))
        {
            return true;
        }

        return file.LastWriteTimeUtc == recorded.UtcDateTime;
    }

    private static string? ReadString(SkyCD.Documents.Collections.PropertiesCollection properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}

public enum MediaMetadataSkipReason
{
    None,
    NoSourcePath,
    NoProvider,
    SourceUnavailable,
    SourceChanged,
    Failed
}

public sealed record MediaMetadataOutcome(string EntryId, bool Stored, MediaMetadataSkipReason SkipReason, string? FailureReason)
{
    public static MediaMetadataOutcome Skipped(string entryId, MediaMetadataSkipReason reason) =>
        new(entryId, Stored: false, reason, FailureReason: null);
}
