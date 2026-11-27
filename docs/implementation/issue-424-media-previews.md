# Issue #424: Offline previews and extracted metadata — feature completion

Parent feature for #435–#442 (all CLOSED). This record closes the parent by mapping
each child to its delivered implementation and coverage, plus the cross-cutting
migration, compatibility and limitation notes the parent requires.

## Sub-task delivery map

- #435 plugin capabilities: `IMediaMetadataCapability` / `IMediaPreviewCapability`
  and the `MediaMetadata*` contracts in
  [MediaContracts.cs](https://github.com/SkyCD/SkyCD.SDK/blob/main/Capabilities/Media/MediaContracts.cs); host routing
  via `MediaExtensionManager`. Documented in `docs/plugins/media-capabilities.md`.
- #436 offline persistence: `MediaCacheStore`/`CouchbaseMediaCacheStore`,
  `MediaExtractionScheduler`, retention/eviction, bundles, stale/missing handling.
  Covered by `tests/SkyCD.App.Tests/MediaCacheLifecycleIntegrationTests.cs`
  (reopen without source/plugins, uninstall survival, version/fingerprint
  invalidation, failure retention, cancellation, retention budgets).
- #437 browser display: `EntryPreviewViewModel` with localized states, user-annotation
  precedence and offline decoding. Covered by
  `tests/SkyCD.App.Tests/MediaPreviewBrowsingIntegrationTests.cs` (offline browsing,
  localized rows, stale/missing/unsupported/failed states, bounded text, search over
  captured fields without media parsing, never opening the source).
- #438 image: `Plugins/SkyCD.Plugin.Image` (SkiaSharp, MIT) + unit/integration tests.
  `docs/plugins/image.md`.
- #439 audio: `Plugins/SkyCD.Plugin.Audio` (tags + cover art) + tests. `docs/plugins/audio.md`.
- #440 video: `Plugins/SkyCD.Plugin.Video` (container metadata + poster frame via an
  explicitly configured external decoder) + tests. `docs/plugins/video.md`.
- #441 PDF: `Plugins/SkyCD.Plugin.Pdf` (first-page preview + document metadata) +
  tests. `docs/plugins/pdf.md`.
- #442 plain text: `Plugins/SkyCD.Plugin.Text` (bounded previews + searchable
  extraction) + tests. `docs/plugins/text.md`.

Verified 2026-11-17: all five media assemblies build clean and resolve both media
contracts through assembly-metadata discovery (`DiscoveredPluginFactory`, no
`plugin.json`), 15/15 discovery checks passed.

## Migration and compatibility

Media caching is additive: existing catalogs gain previews/metadata on scan without
touching captured paths or hierarchy. Cache records are keyed by source fingerprint
plus provider/schema identity; provider, schema or fingerprint changes mark older
records stale instead of destroying them, and user annotations always win over
re-captured values. Public contracts (`MediaContracts`, cache store interfaces) are
unchanged by this feature.

## Limitations

- Previews/metadata require a scan with a capable provider installed; entries
  captured earlier show unavailable states, never fabricated data.
- Video poster frames need an explicitly configured external decoder where
  documented; PDF/text/image/audio extraction is fully local.
- Artifact budgets bound preview bytes; oversized or missing artifacts are reported,
  not decoded.
- Display strings are localized through the `core.preview.*` catalog (`PreviewKeys`);
  plugin field labels fall back to raw keys when untranslated.
