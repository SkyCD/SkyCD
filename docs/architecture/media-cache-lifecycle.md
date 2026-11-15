# Offline media cache: durable artifacts, extracted metadata and cache lifecycle

Implements #436 (part of #424) on top of the plugin capabilities from #435
(`docs/plugins/media-capabilities.md`). The host owns scheduling, storage and lifecycle; plugins only
produce values and bytes.

Code: `src/SkyCD.Presentation/MediaCache/`.

## Why a separate store

Extraction results are stored in their own records, never in `CatalogDocument.Properties`. That gives
three properties the catalog model cannot provide on its own:

- results survive the media being disconnected and the producing plugin being disabled or uninstalled;
- a refresh can replace captured values without ever touching user comments, tags, ratings, status or
  custom properties;
- one entry can hold results from several providers and schema versions side by side, with explicit
  staleness instead of silent overwrites.

## Identity: fingerprint plus provider version

A cache record is keyed by

```
fingerprint + plugin id + plugin version + capability type + schema id + schema version
```

`MediaSourceFingerprint` derives the fingerprint from catalog-relative facts only: the root ancestor id
(the media or folder root the entry was scanned into), the entry path relative to that root, the entry
size and the scanned last-write time. Absolute paths, drive letters and machine-specific locations are
never part of it, so a catalog moved or restored on another machine keeps matching its cache.
`MediaSourceFingerprint.ForCatalogEntry(entry, catalog)` walks the parent chain; renaming a disc does not
change the fingerprint because the scope is the root's id, not its display name.

Record ids are the SHA-256 of that key, so re-extracting an unchanged source with an unchanged provider
updates one record instead of accumulating copies.

## Record states

| State | Meaning | Payload kept |
|---|---|---|
| `Captured` | Current result from a registered provider for the current fingerprint | yes |
| `Stale` | Source fingerprint changed, or the provider identity is no longer registered (plugin updated, disabled or uninstalled) | yes |
| `Failed` | The provider ran and threw or timed out; `FailureReason` is `failed`, `timeout` or `artifact-limit` | no |
| `Unsupported` | Host marker: no enabled provider matches the source, so passes do not retry forever | no |
| `Missing` | The record exists but its artifact bytes are unavailable or fail digest verification | metadata only |

`Stale` records stay readable: that is what makes previews and metadata available offline. Readers decide
whether to accept them (`MediaEntryMetadataProjection.Project(..., includeStaleRecords: false)` filters
them out). `Failed` results never replace a `Captured` record for the same provider identity; when every
provider fails and captured data exists, the pass reports `SourceUnavailable` and leaves the cache
untouched, which is the disconnected-media case.

## Storage layout

`CouchbaseMediaCacheStore` stores everything in the catalog's Couchbase Lite database:

| Collection | Contents |
|---|---|
| `media_cache_records` | One document per cache record |
| `media_cache_staged` | Records of an unpublished batch, tagged with `BatchId` |
| `media_cache_batches` | Batch state: session id, created/published/discarded |

Artifact bytes are Couchbase Lite **blobs** on the record document under `blob:<artifact key>`. Each
`MediaArtifactReference` stores only portable data — key, content type, length and lowercase hex SHA-256 —
so references stay valid across machines and can be verified without the original media.

`InMemoryMediaCacheStore` implements the same contract for hosts without a catalog database and for tests.
`MediaCacheStore.Create(database)` and `MediaCacheStore.Create(repositoryManager)` pick the backend, the
same way `CatalogScanStagingStore` does. Both share `MediaCacheStoreBase`, which holds the lifecycle rules.

Writes are payload-preserving: saving a record without artifact bytes keeps the blobs already stored for
the references it still declares. State changes therefore never drop a preview.

## Captured metadata versus user annotations

`MediaAnnotationPolicy` classifies catalog properties. Scanner, media-capture and scan-publication keys
(`FullName`, `Size`, `LastWriteTime`, `VolumeLabel`, `ContentsState`, …) are scan facts; everything else —
including `Comments`, `Tags`, `Rating`, `Status` and custom properties — is a user annotation.

`MediaEntryMetadataProjection.Project(entry, records)` merges the two into one ordered view:

- captured values appear in provider precedence order (current before missing before stale, then
  descriptor priority, plugin id, capability type); the first provider that supplies a key wins;
- a user annotation with the same key always wins, and the captured value stays available as
  `CapturedValue` so the UI can show or restore it;
- the record's stored schema fields supply units and types even when the plugin is gone.

Extraction writes nothing back to the entry, so repeated refreshes cannot erode user edits.

## Scheduling, staging and cancellation

`MediaExtractionScheduler.ProcessAsync` runs one entry:

1. resolve the providers that match the source (`MediaExtensionManager.ResolveProviders`, no plugin code
   runs);
2. if every matching provider already has a `Captured` record for this fingerprint, reuse them, touch
   `LastUsedAtUtc` and run nothing — unless `ForceRefresh` is set;
3. otherwise run `MediaExtensionManager.ProcessAsync`, which keeps per-provider isolation, bounds and
   timeouts from #435;
4. convert results into records, stage them into a batch, mark superseded records `Stale` in the same
   batch, then publish.

A batch is the unit of publication. The scheduler opens and publishes its own batch for standalone
refreshes; a scan passes its `BatchId` so extraction results become visible exactly when the scan staging
session is published, and disappear when it is cancelled. Cancellation and exceptions discard an owned
batch, so nothing partial is ever published. `Store.BeginBatch(sessionId)` records the scan staging session
id for traceability, and `CleanupAbandonedBatches` drops leftovers.

Preview payloads larger than `MaxArtifactBytesPerRecord` are not truncated and not stored: the record is
written as `Failed` with reason `artifact-limit`.

## Retention, bounds and cleanup

`MediaCacheRetentionPolicy` defaults: 256 MiB total artifacts, 4 MiB per record, 16 records per entry, no
maximum age, orphan removal on, compaction after cleanup on. `EnforceRetention(policy, entryExists, nowUtc)`
applies, in order:

1. orphaned records — `entryExists` reports the entry is gone from the catalog;
2. expired records — older than `MaxAge`, when configured;
3. oversized records — above `MaxArtifactBytesPerRecord`;
4. per-entry cap — beyond `MaxRecordsPerEntry`, pruning payload-less and stale records before captured ones;
5. total budget — evicting by state rank (failed/unsupported, missing, stale, captured) then least recently
   used, until the total artifact budget is met.

Deleting a catalog entry should call `DeleteRecordsForEntry(entryId)`; periodic maintenance should call
`EnforceRetention` with a predicate backed by the catalog repository. There is no maximum age by default:
offline availability is the point of the cache, so age-based expiry is opt-in.

With the Couchbase backend, deleting a record does not immediately reclaim blob files. `CompactAfterCleanup`
runs `PerformMaintenance(MaintenanceType.Compact)` after a pass that removed records, which is what actually
deletes unreferenced blobs; disable it when cleanup runs on a UI thread.

## Export, backup and recovery

Artifact references are portable, so three recovery paths exist:

- **Native catalog backup.** Artifacts live inside the catalog database as blobs, so copying or backing up
  the database directory carries records and previews together. Nothing references an absolute path, so the
  copy works on any machine. A future native catalog export that writes documents must also export the
  bundle below, or accept that previews are re-extracted on demand.
- **Portable bundle.** `MediaCacheBundle.ExportAsync(directory)` writes `manifest.json` plus
  `artifacts/<sha256>.bin`, de-duplicated by digest. `ImportAsync(directory)` verifies length and digest per
  artifact, restores records, and imports records whose payloads are absent or corrupt as `Missing` instead
  of failing the import. A stored record newer than the bundled one is never overwritten, and a record that
  exceeds the local per-record bound is skipped rather than aborting the import.
- **Re-extraction.** `VerifyArtifacts()` checks every stored payload against its digest and marks records
  `Missing`; the next scheduled pass re-extracts them because `Missing` records are not treated as reusable.
  Importing a bundle that contains the payload flips a `Missing` record back to `Captured`.

## Limitations

- Retention is size- and orphan-driven; there is no per-catalog quota or user-facing cache browser yet.
- Staleness is decided from identity, not from semantic schema compatibility: a plugin that changes a
  field's meaning without bumping its schema version keeps its `Captured` state.
- The scheduler processes one entry per call. Batch scheduling across a scan, progress aggregation and
  display are #437's scope; media-specific providers are #438–#442.
- `EnforceRetention` and `GetStatistics` read all records; that is acceptable at current catalog sizes and
  matches the existing repository patterns, but it is not indexed.
