# PDF document metadata and first-page preview plugin

Implements #441 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Pdf/` → plugin id `SkyCD.Plugin.Pdf`.
Tests: `tests/SkyCD.Plugin.Pdf.Tests/`.

This plugin is deliberately independent of image and video extraction: it claims only `.pdf` /
`application/pdf`, so a PDF is never also routed through the image plugin's reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document metadata and bounded text | `PdfMetadataPlugin : IMediaMetadataCapability` | `skycd.pdf.metadata` v1 |
| First-page thumbnail | `PdfPreviewPlugin : IMediaPreviewCapability` | `skycd.pdf.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a test
asserts none exists.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | Always `pdf` |
| `PageCount` | Integer | – | Pages in the document |
| `PdfVersion` | Number | – | Header version, e.g. `1.7` |
| `PageWidth` / `PageHeight` | Number | `pt` | First page's media box, in points |
| `IsEncrypted` | Boolean | – | Document carries an `/Encrypt` dictionary |
| `Title`, `Author`, `Subject`, `Keywords`, `Creator`, `Producer` | Text | – | Information dictionary |
| `CreatedAt`, `ModifiedAt` | Timestamp | – | Parsed from the PDF date format, offset preserved |
| `TextExcerpt` | Text | – | Bounded plain-text excerpt of the leading pages |
| `TextExcerptTruncated` | Boolean | – | Whether the excerpt was cut short |

A field the document does not carry is **omitted**, never published as an empty string or zero, so "unknown"
stays distinguishable from "none". A textless document (a scan, or a page with no content stream) produces no
`TextExcerpt` and no `TextExcerptTruncated` at all — that absence *is* the "no text layer" state, and it keeps
a search over the excerpt from matching on empty strings.

The excerpt is published through the metadata schema rather than as a second artifact. That makes it
searchable through the browser's generic field provider (#437) alongside every other typed field, and it
keeps the single preview artifact slot for the rendered page.

## Bounded text

The excerpt is bounded by **characters**, not pages: `MaxTextExcerptCharacters` (4096) with
`MaxTextExcerptPages` (5) as a second ceiling, so one dense page cannot blow past the character bound and a
long document cannot walk all of its pages. `TextExcerptTruncated` records which happened, because truncation
changes what search can find: a term past the bound is genuinely absent from the stored data, not absent from
the document.

## No scripts, no external references

Two structural guarantees rather than a policy promise:

- The plugin is handed **bytes through the host's bounded `IMediaSource`, never a path**. A document that
  references another file or a URL has no way to make the plugin fetch it, because the plugin has no
  filesystem or network handle to fetch with.
- Neither library executes document scripts. PdfPig reads structure, the information dictionary and text
  operators; the rasterizer draws page content. No JavaScript engine is involved and no form action is run.

## Encrypted documents

A password-protected PDF raises `PdfAuthenticationRequiredException`, distinct from `PdfDecodeException`,
because the remedy differs: the document is intact, it needs a credential. **No password is requested,
guessed, stored, logged, or passed to the rasterizer**, and the host's failure record carries only a
sanitized classification.

Detection does not depend on one parser's exception type: a `PdfDocumentEncryptedException` maps directly,
and any other open failure on a document whose trailer declares `/Encrypt` maps to the same auth-required
state, so a corrupt file is never mistaken for a locked one or vice versa.

A document that *is* encrypted but opens with an empty user password — common for permission-only
protection — is catalogued normally with `IsEncrypted = true`, which is the honest answer: readable, but
protected.

## Rendering

PDFium (through `Docnet.Core`, MIT over BSD-3) rasterizes the first page and returns raw BGRA pixels, which
are encoded to PNG by SkiaSharp (MIT) — the same encoder #438 already uses, so no second imaging stack is
introduced.

Rendering sits behind `IPdfPageRenderer`, and **every rasterizer failure degrades to "no preview"**: a
missing native library, an unloadable dependency, or a page PDFium refuses to draw all return null, and the
preview capability then returns an empty artifact list. The host stores a captured record with metadata and
no artifact, and the browser shows its honest "no preview stored" state. Losing the thumbnail never costs the
page count, the information dictionary or the text excerpt.

The renderer availability check runs **before** any byte is read, so a host with no rasterizer spends none of
its source budget on previews.

## Resource behaviour

`PdfSourceReader` reads the source once into a buffer in 256 KiB chunks before any library sees it. Both the
parser and the rasterizer take byte arrays, so there is no native callback into managed read code — the
failure mode that #438 had to design around does not arise here — and the budget charge equals the document
size, once. A test asserts exactly that, and that a small document needs few host reads.

`MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources before a byte is
charged, so the host budget is what normally binds. Cancellation is checked before the first read, between
pages during excerpt collection, and before and after rasterizing. A parse or render that ignores
cancellation is still bounded by the host's per-provider timeout, which a test exercises with a deliberately
slow rasterizer.

Malformed input raises `PdfDecodeException`, which the host records as a `failed` record with a sanitized
reason. A bare `%PDF-` header followed by nonsense is caught here, not by the magic-byte check.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `PdfPig` | 0.1.16 | Apache-2.0 | Structure, information dictionary, page geometry, text extraction |
| `Docnet.Core` | 2.6.0 | MIT (over BSD-3 PDFium) | First-page rasterization |
| `SkiaSharp` + native assets | 3.119.4 | MIT | Encode PDFium's raw BGRA raster to PNG |

All on the allow-list in `docs/legal/dependency-license-policy.json`, verified with the same CycloneDX SBOM
the CI license gate runs. `PdfiumViewer` and `Docnet` alternatives that pin Windows-only natives were
rejected; `Ghostscript` bindings were rejected as AGPL.

## Limitations

- PDF only. XPS, EPUB and Office formats are out of scope, and this plugin does not parse them.
- The excerpt is a **text-layer** excerpt. A scanned document with no OCR layer yields no excerpt; the
  plugin performs no OCR.
- The thumbnail is the **first page only**, at one size. There is no per-page preview set and no
  page-selection control.
- `PageWidth`/`PageHeight` describe the first page's media box. A document mixing page sizes reports only
  the first.
- Rendering depends on a native rasterizer loading successfully. Where it cannot, results are metadata-only
  by design rather than failing.
- Encrypted documents that need a real password are never read. There is no credential path by design.

## Testing

`tests/SkyCD.Plugin.Pdf.Tests/` builds **structurally valid PDF documents in memory** — real object table,
cross-reference table with correct byte offsets, and trailer — so the suite commits no binary blobs and every
expected value is known to the test asserting it. Fixtures cover single and multi-page documents, a full
information dictionary with timezone-bearing dates, a textless document, a long single-page text layer, an
`/Encrypt`-protected document, a bare-header malformed document, a truncated document and non-PDF bytes.

Coverage includes page count, version and page geometry; every information-dictionary field; omitted fields
when absent; bounded excerpt with an exact truncation assertion; textless omission; auth-required state for
encrypted documents in both capabilities; malformed, truncated, non-PDF and empty sources; oversized
rejection with zero bytes charged; cancellation with zero bytes charged; schema conformance of every returned
value; source bytes unchanged; the read budget charged exactly once; a real PDFium render producing a PNG
within the pixel bound; out-of-range page handling; rasterizer-absent and rasterizer-returns-nothing paths;
the host's own source budget and timeout around a slow rasterizer; and offline retrieval through the real
scheduler and Couchbase Lite store with the database reopened and the plugin unloaded.
