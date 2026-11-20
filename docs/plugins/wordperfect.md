# WordPerfect document metadata plugin

Implements #737 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.WordPerfect/` → plugin id `SkyCD.Plugin.WordPerfect`.
Tests: `tests/SkyCD.Plugin.WordPerfect.Tests/`.

This plugin is deliberately independent of the PDF and text plugins: it claims only `.wpd` / `.wpt` and
`application/vnd.wordperfect`, so a WordPerfect document is never also routed through another metadata reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document summary metadata | `WordPerfectMetadataPlugin : IMediaMetadataCapability` | `skycd.wordperfect.metadata` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a test
asserts none exists.

## Supported files

| Source | Support |
|---|---|
| WordPerfect 6.0 and 6.1+ documents, stored flat | Header facts and the extended document summary |
| WordPerfect 7 and later, stored as an OLE2 compound file | The `PerfectOffice_MAIN` stream is unwrapped and read the same way |
| WordPerfect 5 documents | Header facts only; that generation has no extended document summary |
| WordPerfect for Mac 2.x–3.5e | Header facts only; its summary structure is out of scope |
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

## Compound documents

WordPerfect 7 and later wrap the document in an OLE2 compound file (the `.doc`/`.xls` container format).
`CompoundFileReader` implements only the read path for one named stream: header, DIFAT, FAT, directory, the
regular sector chain and the mini stream used for streams below 4096 bytes. Every declared sector, stream size
and chain link is checked against the bytes actually present, chain traversal is bounded by the sector count,
and a loop or a pointer outside the file is rejected instead of followed. The reader is in-tree rather than a
package because the usual .NET compound-file library is MPL-2.0, which the repository's BSD-2-Clause outbound
policy does not accept (see [Dependencies and licensing](#dependencies-and-licensing)).

## Encrypted documents

A document whose header declares encryption raises `WordPerfectAuthenticationRequiredException`, distinct from
`WordPerfectDecodeException`, because the remedy differs: the document is intact, it needs a credential. **No
password is requested, guessed, stored, logged, or used to decrypt anything**, and the host's failure record
carries only a sanitized classification. The plugin does not implement WordPerfect's decryption algorithm.

## Resource behaviour

`WordPerfectSourceReader` reads the source once into a buffer in 256 KiB chunks. For a compound source it then
extracts the `PerfectOffice_MAIN` stream in memory; either way the host's budget charge equals the source size,
once, and a test asserts both the single read and the exact charge. The plugin only ever holds bytes and never
a path, so a document that references another file has no way to make it open that file.

`MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources before a byte is charged,
so the host budget is what normally binds. An empty source, cancellation before the first read and cancellation
after reading all charge only what was actually read. Cancellation is checked before the first read, between
chunks and after parsing.

## Dependencies and licensing

This plugin has **no NuGet dependencies**. The Wavefront summary parser, the source reader and the compound-file
reader are implemented in the project.

| Candidate | Version | License | Decision |
|---|---|---|---|
| `OpenMcdf` | 3.4.0 | MPL-2.0 | Rejected. Maintained and `.NET 10`-targeting, but the checked-in OSADL BSD-2-Clause outbound row marks MPL-2.0 incompatible, so the CI license gate rejects it without a policy change. |
| `Bodu.IO.Compound` | 1.3.0 | MIT | Rejected. Reads CFB and targets `.NET 10`, but the package is new, has one maintainer and under 3,000 total downloads, so it adds supply-chain risk that the small read-only reader avoids. |
| `DocWright.Ole` | 1.12.0 | MIT | Rejected for the same adoption and maintenance reasons as `Bodu.IO.Compound`. |
| libwpd | 0.10.3 | MPL-2.0 / LGPL-2.1+ | Reference only, not a dependency. Used to cross-check the header, index and summary layouts; no code is linked or shipped. |

No policy file change is made. The format facts come from the WordPerfect file-format documentation and are
cross-checked against libwpd's reference parser and against real sample documents.

## Limitations

- Only the extended document summary is read. Document text, styles, revision history and embedded objects are
  out of scope; this plugin does not render or extract content.
- User-defined summary tags (101 and above) are not published, because the schema is a fixed cached-data
  contract. Known summary tags are published under stable keys.
- WordPerfect 5 and Mac WordPerfect documents yield header facts only; their summary structures are different
  and are not parsed.
- `FormatVersion` is the header's generation, not a marketing product name. The header does not distinguish
  WordPerfect 7, 8, 9, X3 through X6.
- Character-set coverage is limited to ASCII, Windows-1252 and a best-effort low-byte reading for the remaining
  sets. Symbol and box-drawing characters in summary text may not survive.
- Dates are offsetless local values, as described above.
- Protected documents are never read. There is no credential path by design.
- `.wpt` templates are treated as documents; template-specific metadata beyond the summary is not read.

## Testing

`tests/SkyCD.Plugin.WordPerfect.Tests/` builds **structurally valid WordPerfect documents and compound files
in memory**, so the suite commits no binary blobs and every expected value is known to the test asserting it.
Fixtures cover flat 6.0 and 6.1+ documents, a header-only document, an unsupported WP5 document, every summary
tag, valid and invalid dates, charset 0 and charset 1 text, folded whitespace, first-group-wins, zero-length
and overrunning groups, a summary that falls outside the file, and compound documents with both regular and
mini-stream storage.

Coverage includes the descriptor and schema, every published field, omission of absent fields, date validation,
multinational decoding, all summary-damage paths, encrypted and non-WordPerfect sources, empty, truncated and
oversized inputs with zero bytes charged, cancellation, the exact budget charge and read count, the compound
reader's regular and mini-stream paths, a missing `PerfectOffice_MAIN` stream, the host's extension and content
type matching, standard and opt-in selection, failure classification, assembly discovery without a manifest,
and offline storage through the real scheduler and in-memory cache store.
