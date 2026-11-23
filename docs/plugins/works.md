# Microsoft Works document metadata and preview plugin

Implements #740 and #949 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Works/` → plugin id `SkyCD.Plugin.Works`.
Tests: `tests/SkyCD.Plugin.Metadata.Works.Tests/`.

The plugin claims only `.wps` and `application/vnd.ms-works`, so a Works word processor document is never
routed through another metadata or preview reader. It follows the PDF and RTF plugins' conventions: versioned
schemas, a bounded single-pass source read, omission instead of empty values, a sanitized decode failure, and
a renderer that degrades to "no preview" instead of failing metadata.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document summary metadata | `WorksMetadataPlugin : IMediaMetadataCapability` | `skycd.works.metadata` v1 |
| Document thumbnail | `WorksPreviewPlugin : IMediaPreviewCapability` | `skycd.works.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a test
asserts none exists.

## Supported files

| Source | Support |
|---|---|
| Works 2 / early Works 3, stored flat | Header facts only; no summary and no preview |
| Works 3 and 4, stored as an OLE2 compound file with an `MN0` stream | Header facts, both summary property streams, and a preview from an embedded picture or the single-byte body text |
| Works 2000 (`CONTENTS` stream beginning `CHNKINK`) | Header facts, both summary property streams, and a preview from an embedded picture or the UTF-16 body text |
| Works 7 / 8 (`CONTENTS` stream beginning `CHNKWKS`) | Same as Works 2000 |
| Any other format | Not matched by extension or content type; the plugin also rejects a mismatched header |

Recognition is structural, not extension-based. A flat source must be at least 0x100 bytes, start with a
version byte of 0–5 and the `0xFE` marker. A compound source must contain either an `MN0` stream whose bytes
begin with a Works header, or a `CONTENTS` stream whose first chunk is `CHNKINK` or `CHNKWKS`. Extension
matching only decides whether the host offers the plugin.

The compound header byte states the generation, not a product name, so `FormatVersion` reports what the header
actually says: `2`, `3`, `4`, `2000`, or `7/8`.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `Container` | Text | Always `works` |
| `Storage` | Text | `flat` or `compound`, i.e. how the document was stored |
| `FormatVersion` | Text | Generation reported by the document header |
| `Title`, `Subject`, `Author`, `Keywords`, `Comments`, `Template` | Text | Summary fields of the same name |
| `LastSavedBy`, `RevisionNumber`, `ApplicationName` | Text | Summary fields of the same name |
| `Category`, `Manager`, `Company`, `ContentStatus`, `Language` | Text | Document summary fields of the same name |
| `CreatedAt`, `ModifiedAt`, `LastPrintedAt` | Timestamp | Summary dates, in UTC |
| `PageCount`, `WordCount`, `CharacterCount` | Integer | Summary statistics |

A field the document does not carry is **omitted**, never published as an empty string or zero, so "unknown"
stays distinguishable from "none". Statistics are published only when greater than zero, because a Works
writer stores zero for "not computed" as often as for "empty".

`Title` through `LastPrintedAt` come from the standard `\x05SummaryInformation` property set; `Category`
through `Language` come from `\x05DocumentSummaryInformation`. Works 3 and later write both using the same OLE
property set layout as other desktop applications.

## Preview

The preview capability reads the same bounded source and applies the same structural and encryption rules as
metadata. It publishes at most one artifact and never modifies the document:

1. **Embedded preview.** A compound document that carries a `Preview`, `\x05Preview` or `Thumbnail` stream
   whose bytes begin with a PNG or JPEG signature publishes that stream byte-for-byte with `image/png` or
   `image/jpeg`. The body is not read in this case. A stream larger than 8 MiB or a stream without a known
   image signature is ignored.
2. **Leading text sample.** Otherwise the plugin recovers the first long printable run from the document's
   text-bearing stream. Works 2000 and 7/8 store UTF-16 text in `CONTENTS`; Works 3 and 4 store single-byte
   text in `MN0`, starting at offset `0x100`. A run must contain at least 64 characters and 8 words to count
   as document text, so binary headers, indexes and short strings do not become a thumbnail. Tabs, end-of-line
   and page-break controls become whitespace, the non-breaking hyphen and space controls are preserved, and
   object markers are skipped. Other binary markers end the run instead of being guessed at.
3. **Page render.** The sample is normalized to at most 20,000 characters, split into paragraphs and drawn with
   SkiaSharp on a white page with a 1.4142 aspect ratio. The longest edge is 768 pixels by default. The PNG is
   `image/png`; nothing is rasterized when no text run is found.

The capability returns an **empty artifact list** rather than a failure when there is no honest image to show:
a flat Works 2 file, a body without a readable text run, an empty sample, or a renderer that produces no image.
A damaged compound container still raises `WorksDecodeException`, because the document itself could not be
read. A missing rasterizer also yields an empty list and leaves the source unread, so metadata extraction is
never affected. The host decides whether the absence is presented as "no preview".

Rendering is delegated to `IWorksThumbnailRenderer`, and the renderer catches native-library, font, surface and
encoding failures and returns null. Preview generation never changes metadata extraction or its result.

## Summary parsing

`WorksPropertySet` reads the first section of a property set stream: byte order, set count, section offset,
section size and the property table, ending at the section boundary. The stream's codepage property (id 1)
selects the decoding for `VT_LPSTR` values; `VT_LPWSTR` and `VT_BSTR` are read as UTF-16. `VT_I2`, `VT_UI2`,
`VT_I4` and `VT_UI4` become numbers, and `VT_FILETIME` becomes a UTC timestamp. Vector variants (such as
heading pairs and part titles) are not published.

The parser is deliberately lenient. A malformed header, section, table entry or value ends parsing without
throwing, so a damaged summary stream costs the document its optional fields while the header facts already
known are still returned. Unsupported values are skipped, and an encoding that the runtime cannot resolve
falls back to Windows-1252. Values are folded to single spaces and trimmed; blank strings are dropped.

A damaged document structure is not treated leniently: an unreadable compound container, an `MN0` or
`CONTENTS` stream without the Works header, a compound file with neither stream, a truncated flat source or a
source that is not a Works document at all raises `WorksDecodeException`, which the host records as a `failed`
record with a sanitized reason.

The summary stream is capped at 4 MiB and at 1024 properties. A stream beyond either bound is treated as
absent rather than walked, because a real summary is far smaller and the bounds stop a corrupted size from
costing memory or CPU.

## Compound documents

Works 3 and later wrap the document in an OLE2 compound file (the `.doc`/`.xls` container format).
`CompoundFile` implements only the read path for named streams: header, DIFAT, FAT, directory, the regular
sector chain and the mini stream used for streams below 4096 bytes. Every declared sector, stream size and
chain link is checked against the bytes actually present, chain traversal is bounded by the sector count, and
a loop or a pointer outside the file is rejected instead of followed. Recognition reads only the header prefix
of `MN0` / `CONTENTS`; the preview capability reads the text stream only when the document has no embedded
picture, bounded by `MaxTextBodyBytes`.

The reader is in-tree rather than a package to keep the dependency tree small; the usual .NET compound-file
library is MPL-2.0 (see [Dependencies and licensing](#dependencies-and-licensing)).

## Encryption

The reference parser (libwps) exposes no password handling for Works documents, and Works does not define a
distinct protected-document state inside a readable body that a reader can report without decrypting. The
plugin therefore **never decrypts**: it reads only plain streams. A source that is an OLE encryption wrapper
(an `EncryptionInfo` stream with no `MN0` or `CONTENTS` body, the MS-OFFCRYPTO layout) raises
`WorksAuthenticationRequiredException` from both capabilities, so the host records an auth-required state
instead of a generic decode failure. A document whose payload is encrypted some other way produces a decode
failure, not a false claim that every damaged file needs a password, and no credential is requested, guessed,
stored, logged or used.

## Resource behaviour

`WorksSourceReader` reads the source once into a buffer in 256 KiB chunks, so the host's budget charge equals
the source size, exactly once. The plugin only ever holds bytes and never a path, so a document that
references another file has no way to make it open that file.

`WorksPluginOptions.MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources before
a byte is charged, so the host budget is what normally binds. `MaxPropertyStreamBytes` (4 MiB) bounds each
summary stream; `MaxTextBodyBytes` (8 MiB) bounds the text stream and `MaxEmbeddedImageBytes` (8 MiB) bounds an
embedded image, both treated as absent beyond the bound rather than materialized. `MaxTextCharacters` (20,000)
bounds both the recovered sample and the characters the renderer lays out, and `MaxThumbnailDimension` (768)
bounds the longest edge. An empty source fails with `WorksDecodeException`; cancellation is checked before the
first read, between chunks, and while rendering and laying out text, and a cancelled extraction publishes
nothing.

## Dependencies and licensing

The property set parser, the source reader, the compound-file reader and the text sample reader are implemented
in the project. SkiaSharp draws the PNG thumbnail, exactly as it does for the RTF and PDF plugins.

| Dependency | Version | License | Use |
|---|---|---|---|
| `SkiaSharp` + `SkiaSharp.NativeAssets.{Linux,macOS,Win32}` | 3.119.4 | MIT | Renders the bounded page PNG. Already used by the RTF and PDF plugins, so the native payload adds no new license surface. |

| Candidate | Version | License | Decision |
|---|---|---|---|
| `OpenMcdf` | 3.4.0 | MPL-2.0 | Not adopted. Maintained and `.NET 10`-targeting, but the in-repo read-only reader covers the needed streams while keeping the dependency tree small. MPL-2.0 is Reciprocal/MEDIUM and passes the current license gate. |
| `Bodu.IO.Compound` | 1.3.0 | MIT | Rejected. Reads CFB and targets `.NET 10`, but the package is new, has one maintainer and under 3,000 total downloads, so it adds supply-chain risk that the small read-only reader avoids. |
| `DocWright.Ole` | 1.12.0 | MIT | Rejected for the same adoption and maintenance reasons as `Bodu.IO.Compound`. |
| libwps | 0.4.14 | MPL-2.0 / LGPL-2.1+ | Reference only, not a dependency. Used to cross-check the `MN0` / `CONTENTS` header layouts; no code is linked or shipped. |

No policy file change is made. The format facts are cross-checked against libwps's reference parser.

## Limitations

- Only the standard OLE summary property sets are read as metadata. Document styles, embedded objects and Works
  spreadsheet or database metadata remain out of scope. The preview is an approximation of the leading text,
  not a faithful layout: fonts, columns, tables, pictures and page breaks are not reproduced.
- Works 2 and early Works 3 flat files yield header facts only; they carry no OLE summary and no preview.
- A document body stored in a format the reader cannot interpret yields the explicit "no preview" state, not a
  guessed image. The reader recognizes the text zones of the supported generations only.
- `FormatVersion` is the header's generation, not a marketing product name. Works 7 and 8 both report `7/8`.
- User-defined property identifiers are not published, because the schema is a fixed cached-data contract.
- Dates come from `VT_FILETIME` values, which carry UTC; a writer that stored a nominal local time without a
  timezone cannot be distinguished, so such a value is published as if it were UTC.
- `RevisionNumber` is published as text, because Works writers store it as a string as often as an integer.
- Protected or externally encrypted documents are never decrypted. An MS-OFFCRYPTO wrapper is reported as
  needing a credential; there is no credential path by design.

## Testing

`tests/SkyCD.Plugin.Metadata.Works.Tests/` builds **structurally valid Works documents, property sets and compound files
in memory**, so the suite commits no binary blobs and every expected value is known to the test asserting it.
Fixtures cover flat Works 2 and Works 3 headers, `MN0` Works 3 and 4 documents, `CONTENTS` Works 2000 and 7/8
documents, a stream larger than the mini-stream cutoff so both compound storage paths run, complete and partial
summaries, wide and codepage-selected strings, every published field, leading single-byte and UTF-16 body text,
embedded PNG and JPEG previews, an encryption wrapper, and damaged summaries, bodies and stream chains.

Coverage includes both descriptors and schemas, every published field, omission of absent, blank and zero
values, codepage decoding, integer variants, whitespace folding, all damage paths, non-Works and empty sources,
truncated compound files with zero bytes charged on rejection, cancellation, the exact budget charge and read
count, progress reporting, the rendered PNG bounds, embedded preview content types, the empty "no preview"
states for flat and unreadable bodies, renderer-unavailable behaviour, authentication classification, the
host's extension and content type matching, failure isolation between metadata and preview, assembly discovery
without a manifest, and offline storage through the real scheduler and in-memory cache store.
