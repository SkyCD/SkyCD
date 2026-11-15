# Catalog browser preview surface: offline previews and typed metadata

Implements #437 (part of #424) on top of the durable cache from #436
(`docs/architecture/media-cache-lifecycle.md`) and the plugin capabilities from #435
(`docs/plugins/media-capabilities.md`). The host owns resolution, decoding, bounds and display; plugins
only ever produced the stored values and bytes.

Code:

- `src/SkyCD.Presentation/MediaCache/` — resolution, bounds, decode cache, thumbnail source, search fields
- `src/SkyCD.Presentation/ViewModels/EntryPreviewViewModel.cs` — selection-driven display state
- `src/SkyCD.UI/Controls/Preview/` — the generic pane, the async thumbnail control, localized names
- `src/SkyCD.App/Views/MainWindow.axaml` — the pane under the browser, plus the View menu toggle

## What the surface shows

One generic pane renders whatever the cache holds for the selected entry. It has no knowledge of images,
audio, video, PDF or text as domains: a payload is either a raster image or a bounded text body, decided
from the stored artifact's content type, and the typed fields are whatever schema the producing plugin
declared.

| Stored situation | Pane state | Shown |
|---|---|---|
| Captured record with an image artifact | `Ready` | decoded image, fields |
| Captured record with a `text/*` artifact | `Ready` | bounded text body, fields |
| Captured record with metadata only | `Ready` | "no preview stored" message, fields |
| Only superseded records | `Stale` | the stale payload and fields, marked out of date |
| Record exists, artifact bytes gone | `MissingArtifact` | recovery message, fields |
| Artifact above the display bound | `TooLarge` | size message, fields |
| Every provider failed or timed out | `Failed` | failure message plus sanitized reason |
| Host `unsupported` marker | `Unsupported` | unsupported message |
| No records at all | `Unavailable` | "not captured" message |
| Nothing selected | `NoSelection` | hint |

Field rows carry provenance: a user annotation always wins over the captured value for the same key, and
the captured value stays visible as a hint next to the edit. Values from a superseded record are flagged
as out of date rather than hidden, which is what keeps the pane useful offline.

## Browsing never touches the media

`MediaPreviewResolver` takes an `IMediaCacheStore` and nothing else. It has no stream factory, no path
and no reference to `MediaExtensionManager`, so resolving a selection cannot reconnect a disc or re-run a
decoder even by accident. Payload bytes come from `IMediaCacheStore.TryReadArtifact`, and the record that
served a preview is `Touch`ed so LRU eviction protects what the user is actually looking at.

Because the resolver is plugin-free, a disabled, updated or uninstalled plugin changes nothing about
browsing: its captured records stay readable and are simply reported as `Stale` once the provider
identity no longer matches.

## Bounds and responsiveness

`MediaPreviewDisplayPolicy` sets the display bounds:

- `MaxDecodedArtifactBytes` (8 MiB) — an artifact above it is reported as `TooLarge` and never decoded;
- `MaxTextPreviewCharacters` (8192) — text bodies are cut at the bound and flagged as truncated;
- `ImageCacheEntries` (64) and `ImageCacheBytes` (48 MiB of decoded pixels) — the decode cache budget.

Resolution and decoding both run off the UI thread. `BoundedAsyncCache<TValue>` backs the decode cache and
provides three properties the browser depends on:

- **coalescing** — concurrent requests for one artifact share a single decode, so scrolling a grid does
  not start one decode per item;
- **bounded memory** — least-recently-used eviction inside hard entry and byte budgets, measured on
  decoded pixels rather than encoded size; a single value above the whole budget is served but not kept;
- **per-caller cancellation** — a caller that gives up stops waiting without aborting a load other callers
  are still waiting on.

Selection churn is handled by a generation counter in `EntryPreviewViewModel`: every selection change
cancels the previous load, and a result that arrives after a newer selection started is discarded instead
of overwriting the pane.

## Thumbnails in the browser list

`AsyncThumbnailImage` overlays a cached thumbnail on top of an item's type icon. Only realized containers
request one, so the list's existing virtualization bounds the number of in-flight requests; a request is
cancelled when the container is recycled, re-bound or detached. Until a thumbnail arrives the overlay
stays collapsed and the type icon shows through, so a missing preview is indistinguishable from the
previous behavior.

`BrowserItemsView.ThumbnailProvider` is optional. With no provider the templates render exactly as before,
which is also what the pre-existing browser tests assert.

## Accessibility and localization

Every static label goes through `core.preview.*` (`PreviewKeys`, `Translations/en-Preview.yaml`,
`Translations/lt-Preview.yaml`). Field names resolve through the optional `core.metadata.<Key>` catalog
and fall back to the raw schema key, so a plugin can ship its own translations without a host-side field
list. Values are formatted from the declared `MediaMetadataType` and unit only.

The pane and the thumbnail overlay expose `AutomationProperties.Name`: the preview image is named with the
localized template plus the entry name and re-evaluates on culture change, and the field list, state label
and entry name each carry their own localized name.

## Advanced search

`CatalogFindService.Search(documents, query, fieldProvider)` matches the catalog path as before and
additionally matches fields contributed by an `ICatalogSearchFieldProvider`. A provider opens one
`CatalogSearchFieldLookup` per search pass rather than being called per entry, so `MediaCacheSearchFieldProvider`
reads the stored records once and indexes them by entry — searching a large catalog costs one store pass,
not one query per entry. The lookup contract is plain rendered key/value pairs (`CatalogSearchField`), so
the search never parses media-specific data and a new plugin's schema becomes searchable with no
search-side change. A match reports which field produced it (`CatalogFindResult.MatchedField`), which the
find dialog shows next to the path.

## Limitations

- The pane displays stored results only. Capturing them is `MediaExtractionScheduler` (#436) plus the
  format-specific plugins (#438–#442); with no plugin installed every entry reads `Unavailable`.
- Preview payloads are raster images or text. Vector, audio and video playback are out of scope.
- Thumbnail size is the icon size of the current view mode; the cache stores one decoded image per
  artifact, not one per size.
- The display bounds are host constants, not yet user-configurable options.
- The View menu toggle is per-session; unlike the status bar it is not written to `AppOptionsDocument` yet.
