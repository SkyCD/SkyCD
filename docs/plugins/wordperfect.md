# WordPerfect document metadata and preview plugin

Implements #737, #948 and #1247 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: [SkyCD.Plugin.Metadata.WordPerfect](https://github.com/SkyCD/SkyCD.Plugin.Metadata.WordPerfect) → plugin id `SkyCD.Plugin.WordPerfect`.
Tests: `tests/SkyCD.Plugin.Metadata.WordPerfect.Tests/`.

This plugin is deliberately independent of the PDF and text plugins: it claims only `.wpd` / `.wpt` and
`application/vnd.wordperfect`, so a WordPerfect document is never also routed through another metadata reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document summary metadata | `WordPerfectMetadataPlugin : IMediaMetadataCapability` | `skycd.wordperfect.metadata` v1 |
| Page thumbnail | `WordPerfectPreviewPlugin : IMediaPreviewCapability` | `skycd.wordperfect.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a test
asserts none exists.

## Supported files

| Source | Support |
|---|---|
| WordPerfect 6.0 and 6.1+ documents, stored flat | Header facts, the extended document summary, and a body-text thumbnail |
| WordPerfect 7 and later, stored as an OLE2 compound file | The `PerfectOffice_MAIN` stream is unwrapped and read the same way |
| WordPerfect 5 documents | Header facts and a body-text thumbnail recovered from the WP5 structured records; that generation has no extended summary |
| WordPerfect for Mac 2.x–3.5e | Header facts only; its summary and body structures are out of scope |
| WordPerfect 1–4 | Unsupported: their headers are not recognized, so extraction fails with `WordPerfectDecodeException` |
| Any other format | Not matched by extension or content type; the plugin also rejects a mismatched header |

Recognition is structural, not extension-based: a flat source must begin with the `WPC` header, and a compound
source must contain the `PerfectOffice_MAIN` stream whose bytes begin with that header. Extension matching only
decides whether the host offers the plugin.

The header carries a major and minor version rather than a product name, so `FormatVersion` reports what the
header actually states: `5`, `6.0`, `6.1+`, `Mac 2.x`, `Mac 3.0-3.5`, `Mac 3.5e`, or `unknown`. WordPerfect
7 through X6 all report `6.1+`; the header does not distinguish them.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | Always `wpd` |
| `Storage` | Text | – | `flat` or `compound`, i.e. how the WordPerfect stream was stored |
| `FormatVersion` | Text | – | Generation reported by the header |
| `IsEncrypted` | Boolean | – | Always `false` for a successful extraction |
| `Title` | Text | – | Descriptive name |
| `Subject`, `Author`, `Keywords`, `Category`, `Abstract`, `Comments` | Text | – | Summary fields of the same name |
| `Office`, `Department`, `Division`, `Owner`, `Project`, `Publisher` | Text | – | Organisational summary fields |
| `Language`, `Source`, `Status`, `Security`, `Purpose` | Text | – | Classification summary fields |
| `RevisionNumber`, `VersionNumber`, `DocumentNumber` | Text | – | Summary identifiers; WordPerfect stores these as text |
| `CreatedAt`, `CompletedAt`, `RecordedAt`, `ModifiedAt`, `VersionDate` | Timestamp | – | Summary dates |

A field the document does not carry is **omitted**, never published as an empty string, so "unknown" stays
distinguishable from "none". The first valid group for a tag wins, matching WordPerfect's own writer, and a
date group is only accepted when its year is at least 1900 and its month, day and time values are in range.

WordPerfect stores dates without a trustworthy UTC offset. They are published as nominal local timestamps with
a zero offset rather than being shifted by the machine's current time zone, so the same file always yields the
same value.

## Summary parsing

The extended document summary lives in a prefix packet reached through the document's index table. Each group
is a two-byte total length, a two-byte tag, two bytes of type flags, a null-terminated name and either a
null-terminated text value or a ten-byte date. A zero length ends the packet.

Strings are word strings: the low byte is the character and the high byte selects the WordPerfect character
set. Set 0 and unlisted sets are decoded from the low byte, and set 1 (the multinational set) is decoded as
Windows-1252, which covers accented Latin names. The remaining symbol, box-drawing and Greek sets are not
mapped; summary fields in those sets are decoded best-effort. Line breaks and tabs inside a value are folded
to a single space and outer whitespace is trimmed.

A damaged packet is handled leniently: a group that overruns the packet, an entry whose size falls outside the
file, a truncated table or an index pointer beyond the end of the file all end summary parsing, and the header
facts already known are still returned. This keeps a partly overwritten document catalogued rather than failed.
An unreadable OLE2 container, a compound file without `PerfectOffice_MAIN`, a truncated header or a source that
is not WordPerfect at all raises `WordPerfectDecodeException`, which the host records as a `failed` record with
a sanitized reason.

The packet is capped at 1 MiB and at 4096 groups. A packet beyond either bound is skipped rather than walked,
because a real summary is far smaller and the bounds stop a corrupted size from costing CPU.

## Body text and the page thumbnail

The preview capability hands the extracted `WPC` stream to OfficeIMO's read-only legacy importer
(`LegacyWordImporter`), which walks the prefix packets and body records of WordPerfect 5.x and 6.x and
returns the recovered plain text. The plugin keeps the header, summary, storage and encryption facts on its
own side; the importer only supplies the body.

The importer's own recovery quality gates the preview:

- **Structured** recovery, where the records for the source generation were walked, is drawn. This is the
  result for every WordPerfect 5.x and 6.x body the plugin hands over.
- **Salvage** recovery, where text is extracted conservatively from the document area without the record
  grammar, returns the host's explicit "no preview" artifact list instead of being drawn. Macintosh and
  unknown-generation bodies stay behind that gate, so their text is never guessed at.

The first `MaxRenderedCharacters` characters of the recovered text are trimmed and rendered as a white page
with black sans-serif text by `SkiaWordPerfectThumbnailRenderer`: the page width equals the configured
`MaxThumbnailDimension`, the height follows the A-series page aspect of that width, and lines wrap inside a
proportional margin. The result is a PNG artifact. A renderer that cannot run reports no preview before the
source is read at all, so the document keeps its metadata with no artifact.

Protected and malformed documents behave exactly as they do for extraction: an encrypted header raises
`WordPerfectAuthenticationRequiredException` and a body the importer cannot decode raises
`WordPerfectDecodeException`, so a protected document never shows a partial thumbnail and its failure
classification is unchanged. Macintosh bodies return an empty artifact list, which is the host's explicit
"no preview" state.

The importer never executes macros, scripts or embedded objects and never resolves source links. Cached
WPG1 graphics are the only thing it rasterizes: they are drawn at 96 DPI under `MaxImagePixels` (16 Mi),
i.e. about four US-letter page rasters in total, and their source plus PNG bytes are capped by
`MaxResourceBytes` (64 MiB). Text, record and item budgets follow the source byte count, so a valid
document cannot be rejected by an internal limit below its own size.

The import projects a full editable model rather than only the text, so its cost grows linearly with the
source. Measured on this machine: 4 KiB ≈ 6 ms, 52 KiB ≈ 14 ms, 0.5 MiB ≈ 100 ms, 2.5 MiB ≈ 380 ms per
import. The metadata provider does not import the body, so only the preview pass pays this cost, and the
source itself is already bounded by `MaxSourceBytes`.

## Compound documents

WordPerfect 7 and later wrap the document in an OLE2 compound file (the `.doc`/`.xls` container format).
`CompoundFileReader` implements only the read path for one named stream: header, DIFAT, FAT, directory, the
regular sector chain and the mini stream used for streams below 4096 bytes. Every declared sector, stream size
and chain link is checked against the bytes actually present, chain traversal is bounded by the sector count,
and a loop or a pointer outside the file is rejected instead of followed. The reader is in-tree rather than a
package to keep the dependency tree small; the usual .NET compound-file library is MPL-2.0
(see [Dependencies and licensing](#dependencies-and-licensing)).

## Encrypted documents

A document whose header declares encryption raises `WordPerfectAuthenticationRequiredException`, distinct from
`WordPerfectDecodeException`, because the remedy differs: the document is intact, it needs a credential. **No
password is requested, guessed, stored, logged, or used to decrypt anything**, and the host's failure record
carries only a sanitized classification. The plugin does not implement WordPerfect's decryption algorithm.

## Resource behaviour

`WordPerfectSourceReader` reads the source once into a buffer in 256 KiB chunks. For a compound source it then
extracts the `PerfectOffice_MAIN` stream in memory; either way the host's budget charge equals the source size,
once per provider, and a test asserts both the single read and the exact charge. The plugin only ever holds
bytes and never a path, so a document that references another file has no way to make it open that file.

The preview capability reads through the same reader. When the renderer is unavailable it returns before the
source is opened, so the pass charges nothing and the metadata provider's result is unaffected.

`MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources before a byte is charged,
so the host budget is what normally binds. An empty source, cancellation before the first read and cancellation
after reading all charge only what was actually read. Cancellation is checked before the first read, between
chunks and after parsing.

## Dependencies and licensing

The summary parser, the source reader and the compound-file reader are implemented in the project. The
preview body text comes from OfficeIMO's legacy importer, and the thumbnail is rasterized by SkiaSharp.

| Dependency | Version | License | Purpose |
|---|---|---|---|
| `OfficeIMO.Word` (with `OfficeIMO.Core` and `DocumentFormat.OpenXml`) | 3.6.2 | MIT | Read-only WordPerfect 5.x/6.x body importer. The same shared version every other OfficeIMO plugin uses; it never executes source content and its graphics rasterization is bounded by the plugin's limits. |
| `SkiaSharp` + `SkiaSharp.NativeAssets.{Linux,macOS,Win32}` | 3.119.4 | MIT | Renders the recovered text to a bounded PNG. The same package set is already used by the RTF preview plugin, and a rasterizer failure degrades to "no preview" rather than a failed extraction. |

Earlier evaluations of compound-file packages, and the reference parser used to cross-check the format:

| Candidate | Version | License | Decision |
|---|---|---|---|
| `OpenMcdf` | 3.4.0 | MPL-2.0 | Not adopted. Maintained and `.NET 10`-targeting, but the in-repo read-only reader covers the needed streams while keeping the dependency tree small. MPL-2.0 is Reciprocal/MEDIUM and passes the current license gate. |
| `Bodu.IO.Compound` | 1.3.0 | MIT | Rejected. Reads CFB and targets `.NET 10`, but the package is new, has one maintainer and under 3,000 total downloads, so it adds supply-chain risk that the small read-only reader avoids. |
| `DocWright.Ole` | 1.12.0 | MIT | Rejected for the same adoption and maintenance reasons as `Bodu.IO.Compound`. |
| libwpd | 0.10.3 | MPL-2.0 / LGPL-2.1+ | Reference only, not a dependency. Used to cross-check the header, index and summary layouts; no code is linked or shipped. |

No policy file change is made. The format facts come from the WordPerfect file-format documentation and are
cross-checked against libwpd's reference parser and against real sample documents.

## Limitations

- Only the extended document summary and a leading plain-text sample of the body are read. The thumbnail shows
  text without its formatting: bold, italics, tables' structure, styles, revision history and embedded objects
  are out of scope.
- The preview is available for WordPerfect 5.x and 6.x bodies. Macintosh documents return an empty preview
  because their body grammar is different, and salvage-quality recovery is never drawn.
- Body characters outside ASCII and the single-byte multinational set are recovered only where the importer
  maps the character set; unmapped glyphs become U+FFFD instead of being read as an unrelated code page.
- A body whose record framing is damaged fails the preview with `WordPerfectDecodeException` rather than
  drawing the text recovered before the damage; only a successful structured import is drawn.
- User-defined summary tags (101 and above) are not published, because the schema is a fixed cached-data
  contract. Known summary tags are published under stable keys.
- Mac WordPerfect documents yield header facts only; their summary structures are different and are not
  parsed. WordPerfect 5 has no extended summary, so those documents publish header facts and a thumbnail.
- `FormatVersion` is the header's generation, not a marketing product name. The header does not distinguish
  WordPerfect 7, 8, 9, X3 through X6.
- Character-set coverage is limited to ASCII, Windows-1252 and a best-effort low-byte reading for the remaining
  sets. Symbol and box-drawing characters in summary text may not survive.
- Dates are offsetless local values, as described above.
- Protected documents are never read. There is no credential path by design, and a protected document never
  produces a thumbnail.
- `.wpt` templates are treated as documents; template-specific metadata beyond the summary is not read.

## Testing

`tests/SkyCD.Plugin.Metadata.WordPerfect.Tests/` builds **structurally valid WordPerfect documents and compound files
in memory**, so the suite commits no binary blobs and every expected value is known to the test asserting it.
Fixtures cover flat 6.0 and 6.1+ documents, a header-only document, a WordPerfect 5 document whose body
starts after the header, every summary tag, valid and invalid dates, charset 0 and charset 1 text, folded
whitespace, first-group-wins, zero-length and overrunning groups, a summary that falls outside the file,
and compound documents with both regular and mini-stream storage. Body fixtures encode WP6 text with the
soft-space function and framed variable-length and fixed-length groups, so the importer is exercised
without binary samples.

Coverage includes the descriptor and schemas, every published field, omission of absent fields, date
validation, multinational decoding, all summary-damage paths, encrypted and non-WordPerfect sources, empty,
truncated and oversized inputs with zero bytes charged, cancellation, the exact budget charge and read count,
the compound reader's regular and mini-stream paths, a missing `PerfectOffice_MAIN` stream, the host's
extension and content type matching, standard and opt-in selection, failure classification, assembly discovery
without a manifest, and offline storage through the real scheduler and in-memory cache store.

Preview coverage adds the legacy import (paragraphs, WP5 bodies, multinational bytes, spaces and hyphens,
variable and fixed groups, the character cap, salvage quality returning no preview, and corrupt framing
failing as a decode error), a real SkiaSharp render whose PNG is decoded to assert the configured maximum
width, the option bounds passed to the renderer, textless and Macintosh documents, protected and malformed
sources, a missing renderer with zero bytes charged, a single read and charge, compound-wrapped bodies, and
the host run where metadata and preview are published together, a WP5 document publishes both, or a
malformed body fails only the preview while metadata is unchanged.
