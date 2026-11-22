# Plain-text preview and searchable excerpt plugin

Implements #442 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Text/` → plugin id `SkyCD.Plugin.Metadata.Text`.
Tests: `tests/SkyCD.Plugin.Metadata.Text.Tests/`.

## What it provides

Two independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Typed encoding facts and a bounded excerpt | `TextMetadataPlugin : IMediaMetadataCapability` | `skycd.text.metadata` v1 |
| Plain-text preview artifact | `TextPreviewPlugin : IMediaPreviewCapability` | `skycd.text.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

### Allow-list

Plain text and documentation (`.txt`, `.text`, `.log`), lightweight markup (`.md`, `.markdown`, `.rst`),
tabular text (`.csv`, `.tsv`), configuration (`.ini`, `.cfg`, `.conf`, `.properties`, `.yaml`, `.yml`,
`.toml`, `.json`, `.xml`), common source files (`.c`, `.h`, `.cpp`, `.hpp`, `.cs`, `.java`, `.kt`, `.py`,
`.rb`, `.go`, `.rs`, `.js`, `.mjs`, `.cjs`, `.ts`, `.tsx`, `.jsx`, `.css`, `.scss`, `.html`, `.htm`, `.sh`,
`.bash`, `.ps1`, `.bat`, `.sql`, `.php`).

**Office documents are deliberately absent.** This plugin does not parse compound or zipped document formats
(`.docx`, `.xlsx`, `.pptx`, `.odt`, …) and does not claim them, so such a file is never routed here and never
half-decoded. PDF is #441's concern and is likewise not claimed.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Encoding` | Text | – | `utf-8`, `utf-16le`, `utf-16be`, `utf-32le`, `utf-32be` or `iso-8859-1` |
| `HasByteOrderMark` | Boolean | – | Source opened with a BOM |
| `LineEndings` | Text | – | `lf`, `crlf`, `cr`, `mixed` or `none` |
| `LineCount` | Integer | – | Lines in the scanned text |
| `CharacterCount` | Integer | – | Decoded characters in the scanned text |
| `ByteLength` | Integer | `byte` | **Whole** source size |
| `TextKind` | Text | – | `text`, `markdown`, `csv`, `structured` or `source` |
| `ScanComplete` | Boolean | – | True when the whole source was read |
| `TextExcerpt` | Text | – | Bounded leading excerpt |
| `ExcerptTruncated` | Boolean | – | True when the excerpt is shorter than the document |
| `Title` | Text | – | Markdown first ATX heading, when present |

`Title` is derived only for Markdown. Inferring a title from arbitrary source, CSV or log content would be a
guess, and a guess stored as a fact is worse than an absent field.

## Encoding detection

A byte order mark is authoritative and wins: UTF-32 LE/BE are checked before UTF-16 LE, because the UTF-32 LE
mark begins with the same two bytes. With no BOM, **strict** UTF-8 validation (invalid sequences throw rather
than substitute) decides between UTF-8 and the documented ISO-8859-1 fallback. ISO-8859-1 maps every byte and
therefore never fails, which is what makes it a safe last resort.

Two details that matter in practice:

- A bounded scan window can cut a multi-byte character in half. The trailing partial sequence is trimmed
  before validation *and* before decoding, so a truncated window is still correctly reported as UTF-8
  instead of falling back to ISO-8859-1 and filling the excerpt with replacement characters.
- The window is also trimmed to a character boundary for UTF-16 and UTF-32, whose code units are two and four
  bytes wide.

## Binary rejection

A binary file renamed to `.txt` is **rejected**, not displayed. Detection runs on *decoded characters*, not
raw bytes: tab, line feed, carriage return and form feed are ordinary text, while every other C0 control,
DEL, and the U+FFFD replacement character count against the source. If they exceed
`MaxBinaryControlRatio` (2%) of the inspected window, extraction fails with `BinaryContentRejectedException`.

Checking decoded characters is what lets UTF-16 sources pass at all — half their bytes are NUL, and a
byte-level check would reject every one of them. Rejection also uses its own exception type, so "not text"
stays distinguishable from "text we could not parse".

## Inert by construction

Markup is never interpreted. The excerpt is published as text and the preview artifact is always
`text/plain` re-encoded to UTF-8 without a BOM, whatever the source encoding was. An `.html` file therefore
reaches the browser with its `<script>` tags and links intact *as characters*, and the browser's text preview
renders them inertly (#437 shows `text/*` artifacts in a read-only text surface).

Following embedded references is impossible rather than merely avoided: the plugin is handed bounded bytes
through `IMediaSource` and never a path, so there is nothing to resolve a relative link or an external entity
against. The plugin performs no network or filesystem access of any kind.

Re-encoding to UTF-8 also normalizes the artifact, so a consumer never has to know the source encoding to
display it; the original encoding stays recorded in the metadata schema.

## Bounds, and what truncation means for search

Three independent bounds:

- `MaxExcerptCharacters` (8192) — the excerpt length. Bounded by **characters**, so one enormous single line
  cannot blow past it.
- `MaxScanBytes` (16 MiB) — how much of the file is read and analysed at all. A multi-gigabyte log costs one
  window read; the rest is counted in `ByteLength` but never decoded.
- `MaxSourceBytes` (64 MiB) — sources above this are rejected before a byte is read, leaving the host's own
  budget as the binding limit.

Two flags distinguish exact from partial results, and they answer different questions:

| Flag | Meaning |
|---|---|
| `ScanComplete = false` | The scan window ended before the file did; `LineCount`/`CharacterCount` describe only what was scanned, while `ByteLength` is still the whole file. |
| `ExcerptTruncated = true` | The excerpt is shorter than the text that was scanned, **or** the scan was incomplete. |

**How truncation affects search matching:** the browser's search (#437) matches against the stored
`TextExcerpt` field. A term beyond the excerpt bound is genuinely absent from the stored data, so a search
miss does not prove the term is absent from the file. `ExcerptTruncated` is what lets a consumer say "not
found in the indexed excerpt" instead of "not in this file". An integration test covers exactly this: a
document whose searchable term sits past the bound stores `ExcerptTruncated = true`, is not matched by
search, and still reports its true byte length.

## Resource behaviour

Reading is a single bounded pass over the leading window in 64 KiB chunks, so the charge against the host
budget equals the window, not the file — a test asserts a 4 KiB source charges exactly 4096 bytes and that a
64-byte window charges exactly 64 bytes of a 4 KB file. Cancellation is checked before the first read and
between chunks. Oversized sources are rejected before any read. An empty file is a legitimate empty document
rather than an error: zero lines, zero characters, no excerpt, and no preview artifact.

## Dependencies

None. Encoding detection, decoding, line analysis and binary rejection are all implemented in the plugin, so
there is no transitive dependency whose license the policy gate has to judge.

## Limitations

- Detection covers the BOM'd Unicode forms, UTF-8, and ISO-8859-1 as the documented fallback. Legacy
  code pages (Windows-125x, Shift-JIS, EUC-KR, GB18030) are not detected; a file in one of them is decoded as
  ISO-8859-1 and its `Encoding` field says so.
- No statistical language-based detection. Detection is BOM plus UTF-8 validation, which is deterministic and
  cheap rather than probabilistic.
- Only the leading window is analysed, so `LineCount` and `CharacterCount` of a very large file describe that
  window; `ScanComplete` says when this is the case.
- Markdown title extraction reads the first ATX heading only. Setext headings, YAML front matter and
  non-Markdown markup get no title.
- No syntax-aware processing: line and character counts are textual, and `TextKind` comes from the extension.

## Testing

`tests/SkyCD.Plugin.Metadata.Text.Tests/` builds every fixture in memory, so the suite commits no binary blobs.
Coverage includes BOM and no-BOM detection for UTF-8, UTF-16 LE/BE and the ISO-8859-1 fallback; a UTF-16
source not being mistaken for binary despite its NUL bytes; a scan window cut inside a multi-byte character
still decoding as UTF-8 with no replacement characters; line-ending classification including mixed and none;
line counting with and without a trailing terminator; a 200 000-character single line bounded without
buffering the line; exact excerpt truncation; the scan window reported incomplete with the whole-file byte
length intact; Markdown title extraction and its absence elsewhere; allow-list categories; HTML catalogued as
inert text with markup verbatim; empty files; binary rejection; oversized rejection and cancellation both with
zero bytes charged; source bytes unchanged; option validation; artifacts always UTF-8 without a BOM;
assembly discovery without a manifest; and offline retrieval through the real scheduler and Couchbase Lite
store — reopened with no plugin and no source — asserting the excerpt is browsable as a `text/plain` payload
and searchable through `MediaCacheSearchFieldProvider`, plus the truncation case above.
