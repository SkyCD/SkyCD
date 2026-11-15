using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.MediaCache.Exceptions;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers the offline media cache lifecycle: durable capture, availability without source or plugin,
/// separation from user annotations, invalidation, bounded storage, staged publication and recovery.
/// </summary>
public sealed class MediaCacheLifecycleIntegrationTests : IDisposable
{
    private const string EntryId = "file-track-1";
    private const string SecondEntryId = "file-track-2";
    private const string MediaRootId = "media-root";

    private static readonly byte[] PreviewBytes = [9, 8, 7, 6];

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-mediacache-{Guid.NewGuid():N}");

    private readonly string bundleDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-mediabundle-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public MediaCacheLifecycleIntegrationTests()
    {
        Directory.CreateDirectory(databaseDirectory);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        foreach (var directory in new[] { databaseDirectory, bundleDirectory })
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Couchbase Lite can release file handles slightly after dispose on some systems.
            }
            catch (UnauthorizedAccessException)
            {
                // Blob files can stay locked briefly after the database is closed on Windows.
            }
        }
    }

    [Fact]
    public async Task CapturedPreviewsAndMetadata_SurviveReopen_WithoutSourceOrPlugins()
    {
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var (_, entry) = SeedCatalog(catalog);
            using var store = MediaCacheStore.Create(database);
            var scheduler = CreateScheduler(
                store,
                Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
                Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));

            var outcome = await ExtractAsync(scheduler, entry, catalog.GetAll());

            Assert.True(outcome.Published);
            Assert.False(outcome.Reused);
            Assert.Equal(2, outcome.Records.Count);
            Assert.All(outcome.Records, record => Assert.Equal(MediaCacheRecordState.Captured, record.State));
        }

        // Reopened catalog: the media is disconnected and no plugin is registered any more.
        using (var database = OpenDatabase())
        {
            using var store = MediaCacheStore.Create(database);
            var records = store.GetRecordsForEntry(EntryId);

            Assert.Equal(2, records.Count);
            Assert.All(records, record => Assert.Equal(MediaCacheRecordState.Captured, record.State));

            var metadata = records.Single(record => record.Metadata.Count > 0);
            Assert.Equal("captured comment", metadata.Metadata.Single(value => value.Key == "Comments").Text);
            Assert.Equal(320L, metadata.Metadata.Single(value => value.Key == "Bitrate").Integer);
            Assert.Equal("kbps", metadata.SchemaFields.Single(field => field.Key == "Bitrate").Unit);

            var preview = records.Single(record => record.Artifacts.Count > 0);
            Assert.True(store.TryReadArtifact(preview.Id, "preview:0", out var data));
            Assert.Equal(PreviewBytes, data);
            Assert.Equal(Sha256(PreviewBytes), preview.Artifacts[0].Sha256);

            var fields = MediaEntryMetadataProjection.Project(null, records);
            Assert.Equal("captured comment", fields.Single(field => field.Key == "Comments").Value);
            Assert.Equal(MediaMetadataOrigin.Captured, fields.Single(field => field.Key == "Bitrate").Origin);
        }
    }

    [Fact]
    public async Task CapturedResultsStayReadable_AfterTheProducingPluginIsUninstalled()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        var capturing = CreateScheduler(store, Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()));
        await ExtractAsync(capturing, entry, catalog.GetAll());
        var captured = Assert.Single(store.GetRecordsForEntry(EntryId));

        // The plugin is uninstalled: no provider matches the source any more.
        var withoutPlugin = CreateScheduler(store);
        var outcome = await ExtractAsync(withoutPlugin, entry, catalog.GetAll(), forceRefresh: true);

        var records = store.GetRecordsForEntry(EntryId);
        Assert.Equal(2, records.Count);

        var marker = records.Single(record => record.State == MediaCacheRecordState.Unsupported);
        Assert.Equal(MediaCacheRecord.HostPluginId, marker.PluginId);
        Assert.Equal(MediaCacheRecord.UnsupportedCapability, marker.Capability);

        var stale = records.Single(record => record.State == MediaCacheRecordState.Stale);
        Assert.Equal(captured.Id, stale.Id);
        Assert.Equal("captured comment", stale.Metadata.Single(value => value.Key == "Comments").Text);
        Assert.Contains(stale.Id, outcome.SupersededRecords.Select(record => record.Id));

        var fields = MediaEntryMetadataProjection.Project(entry, records);
        Assert.Equal("captured comment", fields.Single(field => field.Key == "Comments").Value);
        Assert.True(fields.Single(field => field.Key == "Comments").IsFromStaleRecord);
        Assert.Empty(MediaEntryMetadataProjection.Project(entry, records, includeStaleRecords: false));
    }

    [Fact]
    public async Task MarkSuperseded_KeepsOtherPluginResultsCaptured()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        var item = new MediaItem(".mp3", "audio/mpeg", entry.Size);
        var both = CreateScheduler(
            store,
            Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
            Provider("plugin.tags", "1.0.0", new FakeAudioMetadataCapability { SchemaId = "fake.tags" }));
        await ExtractAsync(both, entry, catalog.GetAll());
        Assert.Equal(2, store.GetRecordsForEntry(EntryId).Count(record => record.State == MediaCacheRecordState.Captured));

        // Only plugin.tags is still installed.
        var remaining = CreateScheduler(store, Provider("plugin.tags", "1.0.0", new FakeAudioMetadataCapability { SchemaId = "fake.tags" }));
        var superseded = remaining.MarkSuperseded(EntryId, item);

        var stale = Assert.Single(superseded);
        Assert.Equal("plugin.audio", stale.PluginId);
        Assert.Equal(MediaCacheRecordState.Stale, store.GetRecord(stale.Id)!.State);
        Assert.Equal(
            MediaCacheRecordState.Captured,
            store.GetRecordsForEntry(EntryId).Single(record => record.PluginId == "plugin.tags").State);
    }

    [Fact]
    public async Task RefreshExtraction_NeverOverwritesUserAnnotations()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, seeded) = SeedCatalog(catalog);
        var entry = AddUserAnnotations(
            catalog,
            seeded,
            ("Comments", "user comment"),
            ("Tags", "rock, 90s"),
            ("CustomRating", "5"));
        using var store = MediaCacheStore.Create(database);

        var first = CreateScheduler(store, Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()));
        await ExtractAsync(first, entry, catalog.GetAll());

        // A refresh that captures a different comment for the same key.
        var second = CreateScheduler(
            store,
            Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability { Comment = "second capture" }));
        await ExtractAsync(second, entry, catalog.GetAll(), forceRefresh: true);

        var stored = catalog.Get(EntryId);
        Assert.NotNull(stored);
        Assert.Equal("user comment", stored!.Properties["Comments"]);
        Assert.Equal("rock, 90s", stored.Properties["Tags"]);
        Assert.Equal("5", stored.Properties["CustomRating"]);

        var fields = MediaEntryMetadataProjection.Project(stored, store.GetRecordsForEntry(EntryId));
        var comments = fields.Single(field => field.Key == "Comments");
        Assert.Equal(MediaMetadataOrigin.UserEdited, comments.Origin);
        Assert.Equal("user comment", comments.Value);
        Assert.Equal("second capture", comments.CapturedValue);
        Assert.True(comments.IsUserEdited);

        var bitrate = fields.Single(field => field.Key == "Bitrate");
        Assert.Equal(MediaMetadataOrigin.Captured, bitrate.Origin);
        Assert.Equal(320L, bitrate.Value);
        Assert.Equal("kbps", bitrate.Unit);

        Assert.Equal(MediaMetadataOrigin.UserEdited, fields.Single(field => field.Key == "Tags").Origin);
        Assert.Equal(MediaMetadataOrigin.UserEdited, fields.Single(field => field.Key == "CustomRating").Origin);
        Assert.DoesNotContain(fields, field => field.Key == "FullName");
        Assert.DoesNotContain(fields, field => field.Key == "LastWriteTime");
    }

    [Fact]
    public async Task PluginVersionChange_CapturesANewRecord_AndKeepsThePreviousOneStale()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        var v1 = new FakeAudioMetadataCapability { Comment = "v1 comment" };
        await ExtractAsync(CreateScheduler(store, Provider("plugin.audio", "1.0.0", v1)), entry, catalog.GetAll());
        Assert.Equal(1, v1.ExtractCount);

        var v2 = new FakeAudioMetadataCapability { Comment = "v2 comment" };
        var outcome = await ExtractAsync(CreateScheduler(store, Provider("plugin.audio", "2.0.0", v2)), entry, catalog.GetAll());

        Assert.False(outcome.Reused);
        Assert.Equal(1, v2.ExtractCount);

        var records = store.GetRecordsForEntry(EntryId);
        Assert.Equal(2, records.Count);
        Assert.Equal("v2 comment", records.Single(r => r.State == MediaCacheRecordState.Captured).Metadata.Single(v => v.Key == "Comments").Text);
        Assert.Equal("v1 comment", records.Single(r => r.State == MediaCacheRecordState.Stale).Metadata.Single(v => v.Key == "Comments").Text);

        // The current value wins the projection even though the stale one is still stored.
        var fields = MediaEntryMetadataProjection.Project(entry, records);
        Assert.Equal("v2 comment", fields.Single(field => field.Key == "Comments").Value);

        // A third pass with an unchanged provider reuses the cache and runs no plugin.
        var reuse = await ExtractAsync(CreateScheduler(store, Provider("plugin.audio", "2.0.0", v2)), entry, catalog.GetAll());
        Assert.True(reuse.Reused);
        Assert.Equal(1, v2.ExtractCount);
        Assert.Equal(2, store.GetRecordsForEntry(EntryId).Count);
    }

    [Fact]
    public async Task SchemaVersionChange_InvalidatesTheCachedResult()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        await ExtractAsync(
            CreateScheduler(store, Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability())),
            entry,
            catalog.GetAll());

        var outcome = await ExtractAsync(
            CreateScheduler(store, Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability { SchemaVersion = 2 })),
            entry,
            catalog.GetAll());

        Assert.False(outcome.Reused);
        var records = store.GetRecordsForEntry(EntryId);
        Assert.Equal(2, records.Count);
        Assert.Equal(2, records.Single(r => r.State == MediaCacheRecordState.Captured).SchemaVersion);
        Assert.Equal(1, records.Single(r => r.State == MediaCacheRecordState.Stale).SchemaVersion);
    }

    [Fact]
    public async Task ChangedSourceFingerprint_InvalidatesTheCachedResult()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, seeded) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        var capability = new FakeAudioMetadataCapability();
        var scheduler = CreateScheduler(store, Provider("plugin.audio", "1.0.0", capability));

        await ExtractAsync(scheduler, seeded, catalog.GetAll());
        var originalFingerprint = MediaSourceFingerprint.ForCatalogEntry(seeded, catalog.GetAll());

        // The same file re-scanned with a different size is a different source.
        var resized = new CatalogDocument
        {
            Id = seeded.Id,
            Name = seeded.Name,
            ParentId = seeded.ParentId,
            Type = seeded.Type,
            Size = 65,
            Properties = new PropertiesCollection(seeded.Properties)
        };
        catalog.Save(resized.Id, resized);

        await ExtractAsync(scheduler, resized, catalog.GetAll());

        var records = store.GetRecordsForEntry(EntryId);
        Assert.Equal(2, records.Count);
        Assert.Equal(MediaCacheRecordState.Stale, records.Single(r => r.Fingerprint == originalFingerprint).State);
        Assert.Equal(2, capability.ExtractCount);
        Assert.Single(store.GetRecordsForFingerprint(originalFingerprint));
    }

    [Fact]
    public async Task FailedRefresh_KeepsThePreviouslyCapturedResult()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        var audio = new FakeAudioMetadataCapability();
        var preview = new FakePreviewCapability();
        await ExtractAsync(
            CreateScheduler(store, Provider("plugin.audio", "1.0.0", audio), Provider("plugin.preview", "1.0.0", preview)),
            entry,
            catalog.GetAll());

        preview.Fail = true;
        var outcome = await ExtractAsync(
            CreateScheduler(store, Provider("plugin.audio", "1.0.0", audio), Provider("plugin.preview", "1.0.0", preview)),
            entry,
            catalog.GetAll(),
            forceRefresh: true);

        Assert.Equal(1, outcome.PreservedRecordCount);
        var records = store.GetRecordsForEntry(EntryId);
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(MediaCacheRecordState.Captured, record.State));

        var previewRecord = records.Single(record => record.Artifacts.Count > 0);
        Assert.True(store.TryReadArtifact(previewRecord.Id, "preview:0", out var data));
        Assert.Equal(PreviewBytes, data);
    }

    [Fact]
    public async Task UnavailableSource_KeepsCapturedResults_AndIsReported()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        var capability = new FakeAudioMetadataCapability();
        var scheduler = CreateScheduler(store, Provider("plugin.audio", "1.0.0", capability));
        await ExtractAsync(scheduler, entry, catalog.GetAll());

        // The disc is gone: opening the source fails before any plugin runs.
        var outcome = await ExtractAsync(
            scheduler,
            entry,
            catalog.GetAll(),
            forceRefresh: true,
            openSource: _ => throw new IOException("The device is not ready."));

        Assert.True(outcome.SourceUnavailable);
        Assert.False(outcome.Published);
        Assert.Equal(1, outcome.PreservedRecordCount);

        var record = Assert.Single(store.GetRecordsForEntry(EntryId));
        Assert.Equal(MediaCacheRecordState.Captured, record.State);
        Assert.Equal("captured comment", record.Metadata.Single(value => value.Key == "Comments").Text);
        Assert.DoesNotContain(store.GetAllRecords(), stored => stored.State == MediaCacheRecordState.Failed);
    }

    [Fact]
    public async Task FailedExtraction_WithoutCachedData_IsRecordedAndCanRecoverLater()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        var capability = new FakeAudioMetadataCapability { Fail = true };
        var scheduler = CreateScheduler(store, Provider("plugin.audio", "1.0.0", capability));
        await ExtractAsync(scheduler, entry, catalog.GetAll());

        var failed = Assert.Single(store.GetRecordsForEntry(EntryId));
        Assert.Equal(MediaCacheRecordState.Failed, failed.State);
        Assert.Equal("failed", failed.FailureReason);
        Assert.False(failed.TimedOut);
        Assert.Empty(failed.Metadata);

        // The provider recovers on a later pass without any forced refresh.
        capability.Fail = false;
        await ExtractAsync(scheduler, entry, catalog.GetAll());

        var recovered = Assert.Single(store.GetRecordsForEntry(EntryId));
        Assert.Equal(failed.Id, recovered.Id);
        Assert.Equal(MediaCacheRecordState.Captured, recovered.State);
        Assert.Null(recovered.FailureReason);
    }

    [Fact]
    public async Task UnsupportedSource_IsRecordedOnce_AndNotRewrittenOnLaterPasses()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);

        // The only installed provider handles a different container.
        var scheduler = CreateScheduler(store, Provider("plugin.flac", "1.0.0", new FakeFlacMetadataCapability()));
        var first = await ExtractAsync(scheduler, entry, catalog.GetAll());

        var marker = Assert.Single(store.GetRecordsForEntry(EntryId));
        Assert.Equal(MediaCacheRecordState.Unsupported, marker.State);
        Assert.Equal(MediaCacheRecord.CreateUnsupportedId(first.Fingerprint), marker.Id);
        Assert.True(first.Published);

        var second = await ExtractAsync(scheduler, entry, catalog.GetAll());
        Assert.True(second.Reused);
        Assert.Equal(marker.Id, Assert.Single(store.GetRecordsForEntry(EntryId)).Id);
        Assert.Equal(marker.CapturedAtUtc, Assert.Single(store.GetRecordsForEntry(EntryId)).CapturedAtUtc);
    }

    [Fact]
    public async Task StagedExtraction_BecomesVisibleOnlyWhenTheBatchIsPublished()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        var scheduler = CreateScheduler(
            store,
            Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
            Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));

        var batch = store.BeginBatch("stage-session-1");
        var outcome = await ExtractAsync(scheduler, entry, catalog.GetAll(), batchId: batch.BatchId);

        Assert.False(outcome.Published);
        Assert.Equal(batch.BatchId, outcome.BatchId);
        Assert.Empty(store.GetAllRecords());
        Assert.Equal(2, store.GetStagedRecords(batch.BatchId).Count);
        Assert.Equal("stage-session-1", store.GetBatch(batch.BatchId)!.SessionId);

        var published = store.PublishBatch(batch.BatchId);

        Assert.Equal(2, published.PublishedRecordCount);
        Assert.Equal(1, published.ArtifactCount);
        Assert.Equal(2, store.GetAllRecords().Count);
        Assert.True(store.GetBatch(batch.BatchId)!.IsPublished);
        Assert.Throws<MediaCacheBatchAlreadyPublishedException>(() => store.PublishBatch(batch.BatchId));
        Assert.Throws<MediaCacheBatchAlreadyPublishedException>(() => store.Stage(batch.BatchId, new MediaCachePayload(store.GetAllRecords()[0])));
    }

    [Fact]
    public async Task DiscardedBatch_LeavesNothingPublished()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        var scheduler = CreateScheduler(store, Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()));

        var batch = store.BeginBatch("stage-session-2");
        await ExtractAsync(scheduler, entry, catalog.GetAll(), batchId: batch.BatchId);
        Assert.Single(store.GetStagedRecords(batch.BatchId));

        store.DiscardBatch(batch.BatchId);

        Assert.Empty(store.GetAllRecords());
        Assert.Empty(store.GetStagedRecords(batch.BatchId));
        Assert.True(store.GetBatch(batch.BatchId)!.IsDiscarded);
        Assert.Throws<MediaCacheBatchDiscardedException>(() => store.PublishBatch(batch.BatchId));
        Assert.Throws<MediaCacheBatchNotFoundException>(() => store.GetStagedRecords("missing-batch"));
    }

    [Fact]
    public async Task CancelledExtraction_DiscardsItsOwnBatch()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        using var cancellation = new CancellationTokenSource();
        var scheduler = CreateScheduler(store, Provider("plugin.audio", "1.0.0", new CancellingCapability(cancellation)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExtractAsync(scheduler, entry, catalog.GetAll(), cancellationToken: cancellation.Token));

        Assert.Empty(store.GetAllRecords());
        var batch = Assert.Single(store.GetBatches());
        Assert.True(batch.IsDiscarded);
        Assert.False(batch.IsPublished);
    }

    [Fact]
    public void Retention_EvictsLeastRecentlyUsedRecords_ToStayWithinTheArtifactBudget()
    {
        using var database = OpenDatabase();
        using var store = new CouchbaseMediaCacheStore(
            database,
            new MediaCacheRetentionPolicy(MaxTotalArtifactBytes: 10, MaxArtifactBytesPerRecord: 8, CompactAfterCleanup: false));

        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var oldest = SaveArtifactRecord(store, "entry-1", now.AddDays(-3), now.AddDays(-3));
        var middle = SaveArtifactRecord(store, "entry-2", now.AddDays(-2), now.AddDays(-2));
        var newest = SaveArtifactRecord(store, "entry-3", now.AddDays(-1), now.AddDays(-1));

        Assert.Equal(12, store.GetStatistics().ArtifactBytes);

        var result = store.EnforceRetention(nowUtc: now);

        Assert.Equal(1, result.EvictedRecordsRemoved);
        Assert.Equal(4, result.FreedArtifactBytes);
        Assert.Equal(8, result.RemainingArtifactBytes);
        Assert.Equal(2, result.RemainingRecordCount);
        Assert.Null(store.GetRecord(oldest));
        Assert.NotNull(store.GetRecord(middle));
        Assert.NotNull(store.GetRecord(newest));
    }

    [Fact]
    public void Retention_RemovesOrphanedRecords_AndPrunesStaleRecordsBeforeCapturedOnes()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = new CouchbaseMediaCacheStore(
            database,
            new MediaCacheRetentionPolicy(MaxRecordsPerEntry: 2, CompactAfterCleanup: false));

        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        SaveMetadataRecord(store, entry.Id, "fingerprint-current", MediaCacheRecordState.Captured, now, "2.0.0");
        SaveMetadataRecord(store, entry.Id, "fingerprint-current", MediaCacheRecordState.Stale, now.AddDays(-1), "1.0.0");
        SaveMetadataRecord(store, entry.Id, "fingerprint-current", MediaCacheRecordState.Stale, now.AddDays(-2), "0.9.0");
        SaveMetadataRecord(store, SecondEntryId, "fingerprint-other", MediaCacheRecordState.Captured, now, "1.0.0");

        // The second entry was deleted from the catalog.
        DeleteCatalogDocument(catalog, SecondEntryId);

        var result = store.EnforceRetention(entryExists: id => catalog.Get(id) is not null, nowUtc: now);

        Assert.Equal(1, result.OrphanedRecordsRemoved);
        Assert.Equal(1, result.EntryCapRecordsRemoved);
        Assert.Empty(store.GetRecordsForEntry(SecondEntryId));

        var remaining = store.GetRecordsForEntry(EntryId);
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, record => record.State == MediaCacheRecordState.Captured);
        Assert.Contains(remaining, record => record.PluginVersion == "1.0.0");

        Assert.Equal(2, store.DeleteRecordsForEntry(EntryId));
        Assert.Empty(store.GetRecordsForEntry(EntryId));
        Assert.Equal(0, store.GetStatistics().RecordCount);
    }

    [Fact]
    public void Retention_RemovesExpiredRecords_WhenAMaximumAgeIsConfigured()
    {
        using var database = OpenDatabase();
        using var store = new CouchbaseMediaCacheStore(
            database,
            new MediaCacheRetentionPolicy(MaxAge: TimeSpan.FromDays(1), CompactAfterCleanup: false));

        var now = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var old = SaveMetadataRecord(store, "entry-1", "fingerprint-old", MediaCacheRecordState.Captured, now.AddDays(-10), "1.0.0");
        var recent = SaveMetadataRecord(store, "entry-2", "fingerprint-new", MediaCacheRecordState.Captured, now.AddHours(-1), "1.0.0");

        var result = store.EnforceRetention(nowUtc: now);

        Assert.Equal(1, result.ExpiredRecordsRemoved);
        Assert.Null(store.GetRecord(old));
        Assert.NotNull(store.GetRecord(recent));
    }

    [Fact]
    public async Task OversizedArtifacts_AreRejectedByTheStore_AndRecordedAsFailures()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = new CouchbaseMediaCacheStore(
            database,
            new MediaCacheRetentionPolicy(MaxArtifactBytesPerRecord: 2, CompactAfterCleanup: false));

        var scheduler = CreateScheduler(store, Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));
        await ExtractAsync(scheduler, entry, catalog.GetAll());

        var record = Assert.Single(store.GetRecordsForEntry(EntryId));
        Assert.Equal(MediaCacheRecordState.Failed, record.State);
        Assert.Equal("artifact-limit", record.FailureReason);
        Assert.Empty(record.Artifacts);
        Assert.Equal(0, record.ArtifactBytes);
        Assert.False(store.TryReadArtifact(record.Id, "preview:0", out _));

        var oversized = BuildArtifactRecord("entry-oversized", "fingerprint-oversized", PreviewBytes);
        Assert.Throws<MediaCacheArtifactTooLargeException>(() => store.Save(oversized));
    }

    [Fact]
    public async Task MissingArtifacts_AreDetected_AndRestoredFromAPortableBundle()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        var scheduler = CreateScheduler(
            store,
            Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
            Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));
        await ExtractAsync(scheduler, entry, catalog.GetAll());

        var exported = await new MediaCacheBundle(store).ExportAsync(bundleDirectory);
        Assert.Equal(2, exported.RecordCount);
        Assert.Equal(1, exported.ArtifactCount);
        Assert.Equal(0, exported.SkippedArtifactCount);
        Assert.True(File.Exists(Path.Combine(bundleDirectory, MediaCacheBundle.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(bundleDirectory, MediaCacheBundle.ArtifactsDirectoryName, Sha256(PreviewBytes) + ".bin")));

        // Simulate a restored catalog whose artifact storage did not come along.
        var preview = store.GetRecordsForEntry(EntryId).Single(record => record.Artifacts.Count > 0);
        store.DeleteRecord(preview.Id);
        store.Save(new MediaCachePayload(preview));
        Assert.False(store.TryReadArtifact(preview.Id, "preview:0", out _));

        var integrity = store.VerifyArtifacts();
        Assert.Equal(1, integrity.MissingRecordCount);
        Assert.Equal(1, integrity.VerifiedRecordCount);
        Assert.Contains(preview.Id, integrity.MissingRecordIds);
        Assert.Equal(MediaCacheRecordState.Missing, store.GetRecord(preview.Id)!.State);

        var imported = await new MediaCacheBundle(store).ImportAsync(bundleDirectory);
        Assert.Equal(1, imported.RestoredArtifactCount);
        Assert.Equal(0, imported.MissingArtifactCount);
        Assert.Equal(0, imported.IncompleteRecordCount);

        Assert.True(store.TryReadArtifact(preview.Id, "preview:0", out var restored));
        Assert.Equal(PreviewBytes, restored);
        Assert.Equal(MediaCacheRecordState.Captured, store.GetRecord(preview.Id)!.State);
        Assert.Equal(0, store.VerifyArtifacts().MissingRecordCount);
    }

    [Fact]
    public async Task BundleImport_WithoutArtifactFiles_KeepsRecordsAsMissing()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        var scheduler = CreateScheduler(store, Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));
        await ExtractAsync(scheduler, entry, catalog.GetAll());

        await new MediaCacheBundle(store).ExportAsync(bundleDirectory);
        File.Delete(Path.Combine(bundleDirectory, MediaCacheBundle.ArtifactsDirectoryName, Sha256(PreviewBytes) + ".bin"));

        var record = Assert.Single(store.GetRecordsForEntry(EntryId));
        store.DeleteRecord(record.Id);

        var imported = await new MediaCacheBundle(store).ImportAsync(bundleDirectory);

        Assert.Equal(1, imported.ImportedRecordCount);
        Assert.Equal(1, imported.MissingArtifactCount);
        Assert.Equal(1, imported.IncompleteRecordCount);
        Assert.Equal(MediaCacheRecordState.Missing, store.GetRecord(record.Id)!.State);
        Assert.Equal(1, store.VerifyArtifacts().MissingRecordCount);

        await Assert.ThrowsAsync<MediaCacheBundleManifestNotFoundException>(
            () => new MediaCacheBundle(store).ImportAsync(Path.Combine(bundleDirectory, "missing")));
    }

    [Fact]
    public async Task Statistics_ReportStateAndSizeCounters()
    {
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var (_, entry) = SeedCatalog(catalog);
        using var store = MediaCacheStore.Create(database);
        var scheduler = CreateScheduler(
            store,
            Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
            Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));

        await ExtractAsync(scheduler, entry, catalog.GetAll());

        var statistics = store.GetStatistics();
        Assert.Equal(2, statistics.RecordCount);
        Assert.Equal(1, statistics.EntryCount);
        Assert.Equal(2, statistics.CapturedCount);
        Assert.Equal(4, statistics.ArtifactBytes);
        Assert.True(statistics.MetadataBytes > 0);
    }

    [Fact]
    public async Task InMemoryStore_SharesTheDurableStoreLifecycle()
    {
        using var store = new InMemoryMediaCacheStore();
        var catalog = new List<CatalogDocument>();
        var (root, entry) = BuildCatalogEntries();
        catalog.Add(root);
        catalog.Add(entry);

        var scheduler = CreateScheduler(
            store,
            Provider("plugin.audio", "1.0.0", new FakeAudioMetadataCapability()),
            Provider("plugin.preview", "1.0.0", new FakePreviewCapability()));
        await ExtractAsync(scheduler, entry, catalog);

        Assert.Equal(2, store.GetRecordsForEntry(EntryId).Count(record => record.State == MediaCacheRecordState.Captured));
        var preview = store.GetRecordsForEntry(EntryId).Single(record => record.Artifacts.Count > 0);
        Assert.True(store.TryReadArtifact(preview.Id, "preview:0", out var data));
        Assert.Equal(PreviewBytes, data);

        await ExtractAsync(
            CreateScheduler(store, Provider("plugin.audio", "2.0.0", new FakeAudioMetadataCapability()), Provider("plugin.preview", "1.0.0", new FakePreviewCapability())),
            entry,
            catalog);

        var records = store.GetRecordsForEntry(EntryId);
        Assert.Equal(3, records.Count);
        Assert.Equal(2, records.Count(record => record.State == MediaCacheRecordState.Captured));
        Assert.Equal(1, records.Count(record => record.State == MediaCacheRecordState.Stale));

        var cleanup = store.EnforceRetention(new MediaCacheRetentionPolicy(MaxTotalArtifactBytes: 2, CompactAfterCleanup: false));
        Assert.True(cleanup.EvictedRecordsRemoved >= 1);
        Assert.True(store.GetStatistics().ArtifactBytes <= 2);
    }

    [Fact]
    public void StoreFactory_ResolvesTheCatalogDatabase_FromTheRepositoryManager()
    {
        using var container = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(container);
        var repositoryManager = container.Resolve<RepositoryManager>();

        using var store = MediaCacheStore.Create(repositoryManager);

        Assert.IsType<CouchbaseMediaCacheStore>(store);
        Assert.NotNull(store.GetStatistics());
    }

    private Database OpenDatabase() =>
        new("media-cache-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static (CatalogDocument Root, CatalogDocument Entry) SeedCatalog(CatalogDocumentRepository repository)
    {
        var (root, entry) = BuildCatalogEntries();
        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return (root, entry);
    }

    private static (CatalogDocument Root, CatalogDocument Entry) BuildCatalogEntries()
    {
        var root = new CatalogDocument
        {
            Id = MediaRootId,
            Name = "Mixtape",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var entry = new CatalogDocument
        {
            Id = EntryId,
            Name = "track.mp3",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,
            Size = 64,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Music\Mixtape\track.mp3",
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        return (root, entry);
    }

    private static CatalogDocument AddUserAnnotations(
        CatalogDocumentRepository repository,
        CatalogDocument entry,
        params (string Key, string Value)[] annotations)
    {
        var properties = new PropertiesCollection(entry.Properties);
        foreach (var (key, value) in annotations)
        {
            properties[key] = value;
        }

        var updated = new CatalogDocument
        {
            Id = entry.Id,
            Name = entry.Name,
            ParentId = entry.ParentId,
            Type = entry.Type,
            Size = entry.Size,
            ChildrenCount = entry.ChildrenCount,
            ContentsState = entry.ContentsState,
            Status = entry.Status,
            Properties = properties
        };

        repository.Save(updated.Id, updated);
        return updated;
    }

    private static void DeleteCatalogDocument(CatalogDocumentRepository repository, string documentId)
    {
        using var document = repository.Collection.GetDocument(documentId);
        if (document is not null)
        {
            repository.Collection.Delete(document);
        }
    }

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store, params MediaProvider[] providers) =>
        new(new MediaExtensionManager(providers), store);

    private static MediaProvider Provider(string pluginId, string version, IPluginCapability capability) =>
        new(pluginId, version, capability);

    private static Task<Stream> OpenSource(CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(new byte[64]));

    private static Task<MediaExtractionOutcome> ExtractAsync(
        MediaExtractionScheduler scheduler,
        CatalogDocument entry,
        IReadOnlyList<CatalogDocument> catalog,
        string? batchId = null,
        bool forceRefresh = false,
        Func<CancellationToken, Task<Stream>>? openSource = null,
        CancellationToken cancellationToken = default) =>
        scheduler.ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry,
                openSource ?? OpenSource,
                catalog,
                batchId,
                forceRefresh),
            cancellationToken: cancellationToken);

    private static string SaveArtifactRecord(
        IMediaCacheStore store,
        string entryId,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset lastUsedAtUtc)
    {
        var payload = BuildArtifactRecord(entryId, $"fingerprint-{entryId}", PreviewBytes);
        payload.Record.CapturedAtUtc = capturedAtUtc;
        payload.Record.LastUsedAtUtc = lastUsedAtUtc;
        store.Save(payload);
        return payload.Record.Id;
    }

    private static MediaCachePayload BuildArtifactRecord(string entryId, string fingerprint, byte[] bytes)
    {
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, "plugin.preview", "1.0.0", "Preview", "fake.preview", 1),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = "plugin.preview",
            PluginVersion = "1.0.0",
            Capability = "Preview",
            SchemaId = "fake.preview",
            SchemaVersion = 1,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            LastUsedAtUtc = DateTimeOffset.UtcNow,
            SourceLength = bytes.Length,
            Artifacts =
            [
                new MediaArtifactReference
                {
                    Key = "preview:0",
                    ContentType = "image/png",
                    Length = bytes.Length,
                    Sha256 = Sha256(bytes)
                }
            ]
        };

        return new MediaCachePayload(record, new Dictionary<string, byte[]> { ["preview:0"] = bytes });
    }

    private static string SaveMetadataRecord(
        IMediaCacheStore store,
        string entryId,
        string fingerprint,
        MediaCacheRecordState state,
        DateTimeOffset capturedAtUtc,
        string pluginVersion)
    {
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, "plugin.audio", pluginVersion, "Metadata", "fake.audio", 1),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = "plugin.audio",
            PluginVersion = pluginVersion,
            Capability = "Metadata",
            SchemaId = "fake.audio",
            SchemaVersion = 1,
            State = state,
            CapturedAtUtc = capturedAtUtc,
            LastUsedAtUtc = capturedAtUtc,
            SourceLength = 64,
            Metadata = [new MediaMetadataValueRecord { Key = "Comments", Type = MediaMetadataType.Text, Text = "captured" }]
        };

        store.Save(new MediaCachePayload(record));
        return record.Id;
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed class FakeAudioMetadataCapability : IMediaMetadataCapability
    {
        public string SchemaId { get; init; } = "fake.audio";
        public int SchemaVersion { get; init; } = 1;
        public string Comment { get; init; } = "captured comment";
        public bool Fail { get; set; }
        public int ExtractCount { get; private set; }

        public MediaDescriptor Descriptor => new(
            [".mp3"],
            ["audio/mpeg"],
            new MediaMetadataSchema(
                SchemaId,
                SchemaVersion,
                [new("Comments", MediaMetadataType.Text), new("Bitrate", MediaMetadataType.Integer, "kbps")]));

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            ExtractCount++;
            if (Fail)
            {
                throw new IOException("Private plugin failure");
            }

            progress?.Report(new MediaProgress(1));
            return Task.FromResult(new MediaExtractionResult(
            [
                new("Comments", MediaMetadataType.Text, Text: Comment),
                new("Bitrate", MediaMetadataType.Integer, Integer: 320)
            ]));
        }
    }

    private sealed class FakeFlacMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor => new(
            [".flac"],
            ["audio/flac"],
            new MediaMetadataSchema("fake.flac", 1, [new("Channels", MediaMetadataType.Integer)]));

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaExtractionResult([new("Channels", MediaMetadataType.Integer, Integer: 2)]));
    }

    private sealed class FakePreviewCapability : IMediaPreviewCapability
    {
        public byte[] Bytes { get; init; } = PreviewBytes;
        public bool Fail { get; set; }

        public MediaDescriptor Descriptor => new([".mp3"], [], new MediaMetadataSchema("fake.preview", 1, []));

        public Task<MediaPreviewResult> GeneratePreviewAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new IOException("Private preview failure");
            }

            return Task.FromResult(new MediaPreviewResult([new MediaPreviewArtifact("image/png", Bytes)]));
        }
    }

    private sealed class CancellingCapability(CancellationTokenSource cancellation) : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor => new([".mp3"], [], new MediaMetadataSchema("fake.cancel", 1, []));

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
