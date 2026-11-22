# eComic metadata and covers plugin

Implements #729 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`) and #438
(`docs/plugins/image.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.EComic/` → plugin id `SkyCD.Plugin.Metadata.EComic`.
Tests: `tests/SkyCD.Plugin.Metadata.EComic.Tests/`.

## What it provides

Three independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| ComicInfo metadata, preferred name, writer tags | `EComicMetadataPlugin : IMediaMetadataCapability` | `skycd.ecomic.metadata` v1 |
| First-page cover | `EComicCoverPlugin : IMediaPreviewCapability` | `skycd.ecomic.preview` v1 |
| Configuration surface | `EComicConfigurationPlugin : IPluginConfigurationCapability` | – |

Both extraction capabilities also implement `IPluginSettingsConsumer`, so a saved option applies to the next
indexing pass without a restart. Discovery uses assembly metadata and implemented capability types only —
there is no `plugin.json`, and a test asserts none exists.

Default extensions are `.cbz`, `.cbr` and `.cb7`, with the matching comic MIME types
(`application/vnd.comicbook+zip`, `application/vnd.comicbook+rar`, `application/vnd.comicbook`,
`application/x-cbz`, `application/x-cbr`, `application/x-cb7`). Generic `.zip`/`.rar`/`.7z` stay with the
archive indexing plugins; the user can add or replace comic extensions in the configuration surface.

**Nothing is extracted to disk and no page is ever modified.** `ComicInfo.xml` and the first page are read
into memory and bounded; archive paths are never used to write files, so a hostile entry name has nowhere to
go.

## Metadata schema

| Key | Type | Role | Meaning |
|---|---|---|---|
| `Container` | Text | – | `zip`, `rar`, `7z` or another SharpCompress flavor, detected from the bytes |
| `Series`, `Title`, `Number`, `Volume` | Text | – | ComicInfo elements of the same name |
| `Writer` | Text | Tags | ComicInfo `Writer`; names may be `;` or `,` separated |
| `Publisher`, `Genre`, `LanguageISO`, `Summary` | Text | – | ComicInfo elements of the same name |
| `Year`, `PageCount` | Integer | – | Published only when the value parses to a positive integer |
| `PreferredName` | Text | PreferredName | The `Series` or `Title` value the user chose to show instead of the file name |

A field the document does not carry is **omitted**, never published as an empty string or a zero, so
"unknown" stays distinguishable from "none". `Container` is the only value that is always known.

### Catalog effects the roles request

The host applies nothing that was not published, so both effects follow their option exactly:

- **Preferred name.** With the name source set to *Series* or *Title*, `PreferredName` carries that value and
  the browser shows it in place of the file name. The catalogued `CatalogDocument.Name` is never rewritten —
  merges, renames and deletions keep working on the real name — and the projection is a display-only value
  recomputed from the current record.
- **Writer tags.** With writer tags enabled, the host creates any missing tag and assigns it to a newly
  published entry that has no tags yet. Entries the user has already tagged are left untouched, so a rescan
  cannot fight manual organization.

Because the plugin's settings participate in the provider identity, changing an option makes earlier cached
results stale like a schema change does; a rescan re-extracts with the new options instead of silently
serving old values.

## Settings

Enable the plugin to show **Options → eComic Metadata**. Its embedded draft survives navigation;
Options Save persists and applies it, while Cancel/close leaves stored settings unchanged.
See [SDK configuration contracts](sdk-contracts.md#embedded-options-configuration-v3-migration-828).

Persisted by the host's plugin configuration store under the plugin id:

| Key | Values | Default |
|---|---|---|
| `collectionEnabled` | `true`/`false` | `true` |
| `nameSource` | `filename`, `series`, `title` | `filename` |
| `tagWriters` | `true`/`false` | `false` |
| `extractCovers` | `true`/`false` | `false` |
| `coverSize` | 64–2048 pixels, longest edge | `720` |
| `extensions` | `;`-separated list, with or without the dot | `.cbz;.cbr;.cb7` |

Every value is normalized on read: unreadable booleans, numbers or name sources fall back to the documented
default, the cover size is clamped into range, and extensions gain a leading dot, lose duplicates and invalid
entries and are capped at 32. Disabling collection claims no extensions at all, so neither capability runs.

## Cover handling

The first page in natural page order (so `page2` precedes `page10`) is decoded and re-encoded as an opaque
JPEG whose longest edge is at most `coverSize`. Smaller pages are never upscaled. The descriptor is marked as
an index-time preview, which is what lets the indexing pass store the cover together with the metadata so the
browser shows artwork without a second request.

A missing page, a page above the byte bound (`EComicPluginOptions.MaxPageBytes`, 8 MiB), a page that decodes
to more than `MaxPagePixels` (48 million) pixels, an undecodable image and a disabled option all produce an
**empty artifact list** — the explicit "no cover" state — rather than a failure. Metadata extraction is never
affected by a cover failure.

## Resource behaviour

SharpCompress needs random access, and a comic archive routinely exceeds the indexing pass's 16 MiB source
budget. The plugin therefore reads through `MediaSourceStream`, a seekable `Stream` over the host's bounded
`IMediaSource`: only the central directory, the ComicInfo entry and the first page are fetched, and every byte
charged counts against the host's cumulative budget exactly once. Nothing buffers the whole file.

`EComicPluginOptions` bounds the work: `MaxComicInfoBytes` (2 MiB), `MaxPageBytes` (8 MiB), `MaxPagePixels`,
`MaxArchiveEntries` (100 000 visited entries), `MaxFieldCharacters` (4096 per value) and `JpegQuality` (85).
Cancellation is checked before the first read, at every visited entry and before decoding or encoding.
Entries marked encrypted are skipped, `__MACOSX/` resource forks are ignored, and a damaged entry only loses
its own value.

A container that cannot be opened reads as **no values** instead of a failure, so one damaged comic cannot
stop a scan. A budget or cancellation exception from the host still propagates, because that is the pass
being cut short rather than the file being unreadable.

ComicInfo parsing is strict about shape and tolerant about content: DTDs are prohibited, no resolver is
installed and the document has a character bound, while malformed XML, unknown elements and unparsable
numbers degrade to `null`.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `SharpCompress` | 0.50.8 | MIT | Reads zip, rar and 7z comic containers |
| `SkiaSharp` | 3.119.4 | MIT | Decodes the page and encodes the cover |
| `SkiaSharp.NativeAssets.Win32` / `.Linux` / `.macOS` | 3.119.4 | MIT | Platform natives |
| `Avalonia` / `Avalonia.Desktop` | 12.1.6 | MIT | Configuration surface only |

All four are already used by the repository (`SkyCD.Plugin.Metadata.Archives`, `SkyCD.Plugin.Metadata.Image` and the plugin
configuration samples), so the plugin introduces no new dependency tree for the policy gate. Every package and
its transitive dependencies resolve to MIT or another license allowed by
`docs/legal/dependency-license-policy.json`; no blocked, unknown or whitelist-dependent license is involved.
The comic container logic, ComicInfo projection and cover sizing are plugin code, so no further package was
considered for them.

## Limitations

- **Container support comes from SharpCompress.** ZIP (`.cbz`) is fully exercised by tests; RAR (`.cbr`) and
  7z (`.cb7`) go through the same reader but the suite builds no such fixtures, because SharpCompress cannot
  write either format and committing binary archives is not an option. Those paths are covered by the
  constructor's signature detection and by `SkyCD.Plugin.Metadata.Archives`'s existing use of the same package.
- **ComicInfo is the only metadata source.** Archives without `ComicInfo.xml` report only `Container`; there
  is no filename-based guessing of series, number or volume.
- **The cover is the first page in natural name order**, not a user selection and not a ComicInfo-declared
  cover. Multi-page spread detection and reading order inside folders are not modelled.
- **Solid 7z archives** may require SharpCompress to decompress an earlier block before reaching the first
  page; the source budget may cut that short, in which case the pass fails rather than returning partial
  values.
- **Preferred names require a rescan** after changing the name source, because cached metadata carries the
  values captured under the old settings; the settings digest makes the old record stale so the rescan
  re-extracts.
- **Writer tags are applied once.** An entry that already has any tag is skipped; this is deliberate, but it
  means writers are not added to entries organized by hand.

## Testing

`tests/SkyCD.Plugin.Metadata.EComic.Tests/` builds archives and pages in memory — a real zip with ComicInfo and JPEG
pages encoded by SkiaSharp — so the suite commits no binary blobs and every expected value is known to the
test that asserts it. Coverage includes:

- metadata extraction with types and values, Container detection, and the role fields appearing only while
  their option is enabled;
- preferred name per name source and its omission when the selected value is missing;
- missing, malformed and oversized ComicInfo; truncated values; damaged and empty sources;
- settings defaults, round-trip, clamp and normalization, plus the configuration surface and its descriptor;
- cover sizing without upscaling, natural page order, disabled options, pixel and byte bounds, missing pages,
  damaged archives and cancellation;
- host-level extension matching, provider selection, index-time cover storage through the real scheduler and
  in-memory store, offline resolution through `MediaPreviewResolver`, the catalog effects (display name and
  writer tags), settings revisions, assembly discovery without a manifest, and the absence of `plugin.json`.
