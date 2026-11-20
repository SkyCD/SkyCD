# Rich Text Format (RTF) document metadata and preview plugin

Implements #735 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Rtf/` → plugin id `SkyCD.Plugin.Rtf`.
Tests: `tests/SkyCD.Plugin.Rtf.Tests/`.

This plugin claims only `.rtf` / `application/rtf` / `text/rtf`, so a document is never also routed
through the plain-text reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document metadata, statistics and bounded text | `RtfMetadataPlugin : IMediaMetadataCapability` | `skycd.rtf.metadata` v1 |
| Thumbnail of the leading text | `RtfPreviewPlugin : IMediaPreviewCapability` | `skycd.rtf.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | Always `rtf` |
| `RtfVersion` | Integer | – | Version from the header (`\rtf1` → `1`) |
| `AnsiCodePage` | Integer | – | Declared `\ansicpg`, when present |
| `IsProtected` | Boolean | – | Document carries a protection record |
| `Title`, `Subject`, `Author`, `Manager`, `Company`, `Operator`, `Category`, `Keywords`, `Comment` | Text | – | Information group |
| `RevisionNumber` | Integer | – | `\version` |
| `EditingTimeMinutes` | Integer | `min` | `\edmins` |
| `PageCount`, `WordCount`, `CharacterCount`, `CharacterCountWithSpaces` | Integer | – | Document statistics recorded by the writing application |
| `ParagraphCount` | Integer | – | Paragraphs with visible content, computed from the body |
| `CreatedAt`, `ModifiedAt`, `LastPrintedAt` | Timestamp | – | `\creatim`, `\revtim`, `\printim` |
| `TextExcerpt` | Text | – | Bounded plain-text excerpt of the opening paragraphs |
| `TextExcerptTruncated` | Boolean | – | Whether the excerpt was cut short |

A field the document does not carry is **omitted**, never published as an empty string or zero, so "unknown"
stays distinguishable from "none". A textless document produces no `TextExcerpt` and no
`TextExcerptTruncated` at all — that absence *is* the "no text" state, and it keeps a search over the
excerpt from matching on empty strings.

`PageCount`, `WordCount` and the character counts are the values the writing application recorded in the
information group. They are not recomputed: a document edited outside a word processor can disagree with
its own recorded statistics, and publishing the recorded value is the honest reading of the file.

## Reading strategy

The parser is deliberately narrow: it tracks group nesting, destination names and the escaped text of the
information group, and hands the body to the existing `SkyCD.Formatting.Rtf.RtfReader`. Unknown
destinations are skipped, which keeps a malformed or vendor-specific group from discarding the fields around
it. A document truncated mid-body still yields the header, the information group and the paragraphs that
were complete.

Text escapes are decoded with the declared ANSI code page, falling back to Windows-1252 when the declared
page is unavailable. `\uN` values are preferred over their ANSI fallback, which is skipped rather than
duplicated. Typographic control words (`\ldblquote`, `\endash`, `\bullet`, …) map to their Unicode
characters.

## Excerpt bound

The excerpt is bounded by **characters**: `MaxTextExcerptCharacters` (4096). Bullets are rendered as `• `,
paragraphs are separated by newlines, and `TextExcerptTruncated` records whether the bound cut the text
short, because truncation changes what search can find: a term past the bound is genuinely absent from the
stored data, not absent from the document.

## Protected documents

RTF protection can be a form, annotation or read protection, or a password hash recorded by the writing
application. The plugin identifies all of these and publishes `IsProtected = true`, then continues to read
the information group and the body.

That is deliberate: RTF protection is not encryption. The text is present and readable in the file, and it
is what a cataloguer needs. **No password is ever requested, guessed, stored or logged**, and nothing is
written back to the source.

## Rendering

The parsed document is drawn to a bounded PNG by SkiaSharp (MIT) — the same imaging stack the image and PDF
plugins already use, so no second imaging stack is introduced. The preview is a text layout, not a full page
fidelity render: bold, italic, underline and strikeout are carried through, paragraphs and bullets are laid
out top-down, and the output stops at `MaxRenderedCharacters` (20000) so a very long document cannot make
rendering unbounded.

Rendering sits behind `IRtfThumbnailRenderer`, and **every rendering failure degrades to "no preview"**: a
missing native library or an undrawable document returns null, and the preview capability then returns an
empty artifact list. The host stores a captured record with metadata and no artifact, and the browser shows
its honest "no preview stored" state. Losing the thumbnail never costs the metadata. The renderer
availability check runs **before** any byte is read, so a host with no rasterizer spends none of its source
budget on previews.

## Resource behaviour

`RtfSourceReader` reads the source once into a buffer in 256 KiB chunks before the parser sees it, verifies
the `{\rtf` signature, and charges the source exactly once. `MaxSourceBytes` (64 MiB, above the host's 16 MiB
default) rejects oversized sources before a byte is charged, so the host budget normally binds. Cancellation
is checked before the first read, between paragraphs during excerpt collection, and inside rendering. A
render that ignores cancellation is still bounded by the host's per-provider timeout.

Malformed input raises `RtfDecodeException`, which the host records as a `failed` record with a sanitized
reason.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `SkiaSharp` + native assets | 3.119.4 | MIT | Encode the bounded text layout to PNG |

The parser is the repository's own `SkyCD.Formatting.Rtf`, so no RTF library was added. SkiaSharp and its
native assets are on the allow-list in `docs/legal/dependency-license-policy.json`, already used by the
image and PDF plugins.

## Limitations

- RTF only. Word `.doc`, WordprocessingML and OpenDocument text are out of scope.
- The thumbnail is a text layout of the leading paragraphs, not a page-fidelity render. Tables, floating
  frames, images and shapes embedded in the document are not drawn.
- Document statistics are the values recorded by the writing application; the plugin does not recompute
  them.
- RTF timestamps carry no timezone. They are published with a UTC offset because the schema requires one;
  the original wall-clock values are preserved.
- A password-hash protection record can only be detected, never unlocked. The plugin never asks for a
  password and never decrypts anything (RTF protection is not encryption).
- Windows-1252 is the fallback for an unavailable declared code page. Text in an exotic code page with no
  code-pages provider installed may decode approximately.

## Testing

`tests/SkyCD.Plugin.Rtf.Tests/` builds **structurally valid RTF documents in memory**, so the suite commits
no binary blobs. Fixtures cover a full information group with statistics and timestamps, an information
group with escaped and typographic text, a protected document, a document with no information group, a
textless document, a long single paragraph, a truncated document and non-RTF bytes.

Coverage includes every information-group field; omitted fields when absent; protection detection;
timestamp parsing; bounded excerpt with an exact truncation assertion; textless omission; malformed,
truncated, non-RTF and empty sources; oversized rejection with zero bytes charged; cancellation with zero
bytes charged; schema conformance of every returned value; source bytes unchanged; the read budget charged
exactly once; a real Skia render producing a PNG within the pixel bound; renderer-absent and
renderer-returns-nothing paths; the host's source budget and timeout around a slow renderer; failure
isolation between the two capabilities; and assembly discovery without a manifest.
