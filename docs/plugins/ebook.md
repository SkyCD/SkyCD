# eBook metadata and covers plugin

Implements #730 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`) and #438
(`docs/plugins/image.md`).

Assembly: `Plugins/SkyCD.Plugin.EBook/` → plugin id `SkyCD.Plugin.EBook`.
Tests: `tests/SkyCD.Plugin.EBook.Tests/`.

## What it provides

Three independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Book metadata, author tags, preferred name | `EBookMetadataPlugin : IMediaMetadataCapability` | `skycd.ebook.metadata` v1 |
| Embedded cover | `EBookCoverPlugin : IMediaPreviewCapability` | `skycd.ebook.preview` v1 |
| Configuration surface | `EBookConfigurationPlugin : IPluginConfigurationCapability` | – |

Both extraction capabilities also implement `IPluginSettingsConsumer`, so a saved option applies to the next
indexing pass without a restart. Discovery uses assembly metadata and implemented capability types only —
there is no `plugin.json`, and a test asserts none exists.

Default extensions are `.epub`, `.ibook`, `.mobi`, `.prc`, `.azw`, `.azw3` and `.fb2`, with the matching book
MIME types (`application/epub+zip`, `application/x-ibooks+zip`, `application/x-mobipocket-ebook`,
`application/vnd.amazon.ebook`, `application/x-mobi8-ebook`, `application/x-fictionbook+xml`,
`application/x-fictionbook`). Generic `.zip` and `.xml` stay with the archive indexing plugins; the user can
add or replace e-book extensions in the configuration surface.

**Nothing is extracted to disk and no book is ever modified.** The package document, EXTH records and cover
image are read into memory and bounded; archive paths are only used to look entries up, never to write files,
so a hostile entry name has nowhere to go.

## Formats

The container is detected from the leading bytes, not from the extension, so a mislabelled book is still
recognized. The catalogued extension only refines the logical format name.

| Container | Recognized by | Logical `Format` |
|---|---|---|
| ZIP (EPUB, iBook) | `PK` signature | `epub`, or `ibook` for `.ibook` |
| Palm database (Mobipocket, Amazon, PalmDOC) | `BOOK`/`MOBI` or `TEXt`/`REAd` type/creator | `mobi`, `kf8`, `palmdoc` |
| XML (FictionBook) | leading `<`, optionally after a BOM or whitespace | `fb2` |

- **EPUB / iBook** — `META-INF/container.xml` names the package document; when the container is missing or
  unreadable the shallowest `.opf` in the archive is used instead. Dublin Core metadata, the EPUB 2
  `<meta name="cover">` pointer, the EPUB 3 `properties="cover-image"` manifest item and the guide
  `type="cover"` reference are all honoured. `META-INF/encryption.xml` marks the book `Protected` unless every
  encrypted resource is a font (IDPF font obfuscation is not DRM).
- **Mobipocket / Amazon** — the PalmDOC header, MOBI header and EXTH records are parsed read-only. Full name,
  EXTH title, authors, publisher, description, ISBN, ASIN, subject, language and date are read; record 121
  marks a KF8 book, and EXTH 203 marks a fake (placeholder) cover that is never used. A PalmDOC record without
  a MOBI header reports `palmdoc` and uses the database name as its title.
- **FictionBook 2** — the `description` block (`title-info`, `publish-info`, `document-info`) is read from a
  safe XML load; the cover is the `binary` element the `coverpage` references, decoded from base64 with a
  length bound first. A bare `<year>` is published as `PublishedYear` rather than completed into an invented
  date.

A container that cannot be opened, a truncated file and a document with the wrong structure all read as **no
values** instead of a failure, so one damaged book cannot stop a scan. A budget or cancellation exception from
the host still propagates, because that is the pass being cut short rather than the file being unreadable.

## Metadata schema

| Key | Type | Role | Meaning |
|---|---|---|---|
| `Container` | Text | – | `zip`, `palmdb` or `xml`, detected from the bytes |
| `Format` | Text | – | `epub`, `ibook`, `mobi`, `kf8`, `palmdoc` or `fb2` |
| `Title` | Text | – | The book title the document carries |
| `Authors` | Text | Tags | Every author, `; `-separated so the host can create one tag per name |
| `Publisher`, `Language`, `Subject`, `Description` | Text | – | Matching metadata values |
| `Identifier` | Text | – | The first identifier, such as an ISBN, an ASIN or a package unique id |
| `PublishedAt` | Timestamp | – | A full publication date, normalized to UTC |
| `PublishedYear` | Integer | – | The publication year, published separately so a year-only source stays a year |
| `Protected` | Boolean | – | Published only when the content is encrypted, never as `false` |
| `PreferredName` | Text | PreferredName | The author or title value the user chose to show instead of the file name |

A field the document does not carry is **omitted**, never published as an empty string or a zero, so
"unknown" stays distinguishable from "none". `Container` and `Format` are the values that are always known.
A bare year is never turned into a January 1 timestamp: dates are parsed from the forms e-books actually
carry, and a partial date is dropped rather than completed.

### Catalog effects the roles request

The host applies nothing that was not published, so both effects follow their option exactly:

- **Preferred name.** With the name source set to *Author* or *Title*, `PreferredName` carries that value and
  the browser shows it in place of the file name. The catalogued `CatalogDocument.Name` is never rewritten —
  merges, renames and deletions keep working on the real name — and the projection is a display-only value
  recomputed from the current record.
- **Author tags.** With author tags enabled, the host creates any missing tag and assigns it to a newly
  published entry that has no tags yet. Entries the user has already tagged are left untouched, so a rescan
  cannot fight manual organization.

Because the plugin's settings participate in the provider identity, changing an option makes earlier cached
results stale like a schema change does; a rescan re-extracts with the new options instead of silently
serving old values.

## Settings

Enable the plugin to show **Options → eBook Metadata**. Its embedded draft survives navigation;
Options Save persists and applies it, while Cancel/close leaves stored settings unchanged.
See [SDK configuration contracts](sdk-contracts.md#embedded-options-configuration-v3-migration-828).

Persisted by the host's plugin configuration store under the plugin id:

| Key | Values | Default |
|---|---|---|
| `collectionEnabled` | `true`/`false` | `true` |
| `nameSource` | `filename`, `author`, `title` | `filename` |
| `tagAuthors` | `true`/`false` | `false` |
| `extractCovers` | `true`/`false` | `false` |
| `coverSize` | 64–2048 pixels, longest edge | `250` |
| `extensions` | `;`-separated list, with or without the dot | `.epub;.ibook;.mobi;.prc;.azw;.azw3;.fb2` |

Every value is normalized on read: unreadable booleans, numbers or name sources fall back to the documented
default, the cover size is clamped into range, and extensions gain a leading dot, lose duplicates and invalid
entries and are capped at 32. Disabling collection claims no extensions at all, so neither capability runs.

## Cover handling

The cover is decoded from the package (EPUB), the first image record (Mobipocket) or the referenced
`binary` element (FictionBook), then re-encoded as an opaque JPEG whose longest edge is at most `coverSize`.
Smaller images are never upscaled. The descriptor is marked as an index-time preview, which is what lets the
indexing pass store the cover together with the metadata so the browser shows artwork without a second
request.

A book without a cover, a fake Mobipocket cover, a cover above the byte bound (`EBookPluginOptions.MaxCoverBytes`,
8 MiB), an image that decodes to more than `MaxCoverPixels` (48 million) pixels, an undecodable image and a
disabled option all produce an **empty artifact list** — the explicit "no cover" state — rather than a
failure. Metadata extraction is never affected by a cover failure.

## Resource behaviour

Every format is read through the host's bounded `IMediaSource`; nothing buffers the whole file unless the
format requires it:

- **EPUB** uses `MediaSourceStream`, a seekable `Stream` over the source, so only the central directory, the
  container/package parts and the cover entry are fetched. Every byte charged counts against the host's
  cumulative budget exactly once.
- **Mobipocket** reads the record table and the book record once, then only the record that may hold the
  cover. Encrypted text is not decrypted; the header still yields the metadata.
- **FictionBook** must be read whole, so the source is bound by `MaxSourceBytes` before any byte is read.

`EBookPluginOptions` bounds the work: `MaxSourceBytes` (64 MiB), `MaxMetadataPartBytes` (2 MiB for one
package part or book record), `MaxArchiveEntries` (20 000 visited entries), `MaxCoverBytes` (8 MiB),
`MaxCoverPixels` (48 million), `MaxFieldCharacters` (4096 per value) and `JpegQuality` (85). Cancellation is
checked before the first read, while reading chunks, at every visited archive entry and before decoding or
encoding.

XML in EPUB and FictionBook is parsed with DTD processing prohibited, no resolver, comments and processing
instructions ignored, and a character bound, so a document type declaration cannot be used to expand an
entity or reach the network.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `SkiaSharp` | 3.119.4 | MIT | Decodes the cover and encodes the thumbnail |
| `SkiaSharp.NativeAssets.Win32` / `.Linux` / `.macOS` | 3.119.4 | MIT | Platform natives |
| `Avalonia` / `Avalonia.Desktop` | 12.1.6 | MIT | Configuration surface only |

ZIP reading uses `System.IO.Compression` from the framework and the ZIP, Palm database, Mobipocket and
FictionBook parsing is plugin code, so no archive or e-book package was added. SkiaSharp and Avalonia are
already used by the repository (`SkyCD.Plugin.Image` and the plugin configuration samples), so the plugin
introduces no new dependency tree for the policy gate. Every package and its transitive dependencies resolve
to MIT or another license allowed by `docs/legal/dependency-license-policy.json`; no blocked, unknown or
whitelist-dependent license is involved.

## Limitations

- **Metadata sources are the book's own documents.** EPUB relies on the package document, Mobipocket on the
  Palm database record and FictionBook on the description block; there is no filename-based guessing of
  titles, authors or series.
- **DRM is reported, not removed.** An encrypted book still publishes the metadata its headers carry, but the
  encrypted text and any encrypted cover are not decrypted, so its cover is normally absent.
- **KF8 and MOBI7 combined files** report the KF8 format from the boundary marker; the MOBI7 half's metadata
  is not merged with the KF8 EXTH set.
- **Mobipocket covers are found by signature scanning** the candidate image records, because the cover offset
  is relative to the first image record and some files miscount it. A record that does not begin with a
  recognizable JPEG, PNG, GIF, BMP or WebP signature is skipped.
- **FictionBook galleries are not catalogued.** Only the binary the cover page references is decoded, and its
  encoded length is bounded before decoding.
- **Preferred names require a rescan** after changing the name source, because cached metadata carries the
  values captured under the old settings; the settings digest makes the old record stale so the rescan
  re-extracts.
- **Author tags are applied once.** An entry that already has any tag is skipped; this is deliberate, but it
  means authors are not added to entries organized by hand.

## Testing

`tests/SkyCD.Plugin.EBook.Tests/` builds every fixture in memory — ZIP packages with EPUB 2 and EPUB 3
package documents, Palm databases with MOBI headers and EXTH records, and FictionBook XML with base64
binaries — so the suite commits no binary blobs and every expected value is known to the test that asserts
it. Covers are encoded by SkiaSharp at fixture build time. Coverage includes:

- metadata extraction for EPUB 2, EPUB 3, iBook, Mobipocket (with and without EXTH), KF8, PalmDOC and
  FictionBook, with types, values, date normalization and blank-value omission;
- container fallback when `container.xml` is missing, font obfuscation versus content encryption, DTD
  non-expansion, and truncated, empty, random and oversized sources;
- settings defaults, round-trip, clamp and normalization, plus the configuration surface and its descriptor;
- cover sizing without upscaling, escaped EPUB hrefs, guide-only covers, FictionBook binaries, Mobipocket
  cover records, fake-cover suppression, missing covers, non-image payloads, undecodable images and
  cancellation;
- host-level extension matching, provider selection, index-time cover storage through the real scheduler and
  in-memory store, offline resolution through `MediaPreviewResolver`, the catalog effects (display name and
  author tags), settings revisions, assembly discovery without a manifest, and the absence of `plugin.json`.
