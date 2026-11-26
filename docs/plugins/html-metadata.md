# HTML document metadata plugin

Implements #1240 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Html/` → plugin id `SkyCD.Plugin.Html.Metadata`.
Tests: `tests/SkyCD.Plugin.Metadata.Html.Tests/`.

This plugin is separate from `SkyCD.Plugin.FileFormat.Html`, which is a write-only catalog export
plugin. The metadata plugin claims `.html` and `.htm` (`text/html`) and catalogues documents end to
end. The text plugin also matches `text/html` sources and provides the readable pane excerpt; the
priority described under "Rendered page thumbnail" decides which page image wins.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Metadata, structure and bounded text | `HtmlMetadataPlugin : IMediaMetadataCapability` | `skycd.html.metadata` v1 |
| Rendered page thumbnail | `HtmlPreviewPlugin : IMediaPreviewCapability` | `skycd.html.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists. Both capabilities live in the same assembly and are discovered independently.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `Container` | Text | Always `html` |
| `Title` | Text | Normalized document `<title>` |
| `Description`, `Keywords`, `Author`, `Generator` | Text | Lower-cased `<meta name>` values |
| `Language` | Text | Declared document language (`lang`) |
| `DeclaredCharset` | Text | Declared charset (`<meta charset>` or `http-equiv` content type) |
| `HasBaseHref` | Boolean | A `<base href>` element is present |
| `LinkCount`, `ImageCount`, `ScriptCount` | Integer | `a[href]`, `img` and `script` elements |
| `WordCount`, `CharacterCount` | Integer | Computed from the visible text |
| `TextExcerpt` | Text | Bounded plain-text excerpt of the visible text |
| `TextExcerptTruncated` | Boolean | Whether the excerpt was cut short |

A field the document does not carry is **omitted**, never published as an empty string, so "unknown" stays
distinguishable from "none". The computed counts and `HasBaseHref` are always published, because zero
links or a missing base reference is a fact about the document rather than missing metadata. A textless
document has zero counts and no `TextExcerpt`/`TextExcerptTruncated` at all.

## Structure and text

The document is parsed from the in-memory source string with
`HtmlConversionDocumentOptions.UntrustedDefaults`, so the parse is inert: no network or external file
is fetched and nothing is executed. The semantic model keeps visible text (headings, paragraphs, list
items, quotes, tables) while links become counts. Script and style contents never become text.

Headings open sections, so a section-opening heading is emitted as a heading line rather than lost with
the section title; generated and document titles are not body text. A block that aggregates its children
(a list keeps its items, with markers) is emitted once, and a nested list under its item still follows
it. The excerpt is bounded by characters (`MaxTextExcerptCharacters`, 4096). `TextExcerptTruncated`
records whether the bound cut the text short, because truncation changes what search can find: a term
past the bound is genuinely absent from the stored data, not absent from the document.

## Rendered page thumbnail

`HtmlPreviewPlugin` draws the leading content of the document as an A-series page and publishes exactly
one `image/png` artifact. The page is not a rendering of the source: no browser engine runs and nothing
is executed, the visible text of the leading blocks is drawn with headings emphasized. Sections and
blocks are walked in document order and their visible text is drawn — headings larger and bold, body
text as normal text.

The page width is `MaxThumbnailDimension` (768) and the height follows the A-series ratio; the margin is a
twelfth of the width, the body font a fortieth, and drawing stops at the bottom margin. The lines handed to
the renderer are bounded by `MaxRenderedBlocks` (200) and `MaxRenderedCharacters` (6000), so even a huge
document costs a bounded parse-to-page step.

The HTML image must win the list thumbnail without taking over the preview pane. The text plugin also
matches `text/html` and publishes its own page image. The HTML descriptor therefore carries
`Priority: 10` against the text plugin's default `0`: the host already prefers a text artifact for the pane,
so the pane keeps the readable excerpt while the higher-priority record supplies the list thumbnail.

There is no failure path for an undrawable document. A missing rasterizer, a surface or font that cannot be
created, an empty or whitespace-only source, a script-only document, or a source already larger than
`MaxSourceBytes` all produce **no preview artifact** instead of an error, and an oversized source is
rejected before a byte is charged. The capability leaves `IndexTimePreview` at its default, like the other
page renderers, so a metadata-only indexing pass does not rasterize documents; the page is produced when a
capture pass requests previews.

## No scripts, no external references

Two structural guarantees rather than a policy promise:

- The plugin is handed **bytes through the host's bounded `IMediaSource`, never a path**. A document that
  links to another file or a URL has no way to make the plugin fetch it, because the plugin has no
  filesystem or network handle to fetch with. Links become counts, nothing more.
- Nothing in the pipeline executes code. Scripts are counted, never run; no browser engine is involved
  and no HTML is rendered.

## Protected documents

HTML has no encryption or password mechanism of its own. A file that is actually protected by an outer
container is not valid text, and the plugin reports `HtmlDecodeException` rather than guessing or
decrypting anything. No password is ever requested, guessed, stored or logged.

## Resource behaviour

`HtmlSourceReader` reads the source once into a buffer in 256 KiB chunks before the parser sees it.
UTF-8 is the default; a UTF-16 byte-order mark selects UTF-16, and bytes that are not valid UTF-8 fall
back to Windows-1252 so legacy single-byte documents still catalogue. Invalid byte sequences that match
neither path, and embedded NUL bytes, are rejected as `HtmlDecodeException`, so a binary file with an
`.html` extension does not produce mojibake metadata. `MaxSourceBytes` (64 MiB, above the host's 16 MiB
default) rejects oversized sources before a byte is charged, and the same bound is handed to the
conversion parser as `MaxInputCharacters`, so the parse itself stays bounded. Cancellation is checked
before the first read, after parsing and while the text is assembled.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `OfficeIMO.Html` | 3.6.2 | MIT | HTML parsing, semantic document model and inert owned DOM snapshots |
| `SkiaSharp` + `NativeAssets.Linux` / `macOS` / `Win32` | 3.119.4 | MIT | Layout and PNG encoding of the page thumbnail |

`OfficeIMO.Html` 3.6.2 is the same OfficeIMO version the IWork plugin pins, so every OfficeIMO package
in the solution stays on one version. Its transitive dependencies (`OfficeIMO.Core`,
`OfficeIMO.Html.AngleSharp`, `OfficeIMO.Html.Core`, `AngleSharp`, `AngleSharp.Css`) are MIT-licensed.
All pass the Trivy license gate in `docs/legal/dependency-policy.md`. SkiaSharp is the same package set,
at the same version, as the RTF, Text and Markdown preview plugins.

## Limitations

- HTML only. XHTML served as XML, MHTML archives and browser-saved complete pages (with sidecar files)
  are out of scope.
- `PublishedAt`-style dates are not extracted: HTML has no standard document-date field the way front
  matter does.
- Word and character counts describe the visible text, not the raw source, so markup is not counted.
- The thumbnail is a reflowed page of the leading content, not a faithful page layout: tables render as
  their cell text and nested lists lose their indentation.
- Meta names outside `description` / `keywords` / `author` / `generator` (Open Graph, Twitter cards,
  viewport) are not published.
- A document that declares a non-UTF-8 charset but is actually UTF-16 without a byte-order mark falls
  back to Windows-1252 rather than failing.

## Testing

`tests/SkyCD.Plugin.Metadata.Html.Tests/` builds HTML sources in memory. Fixtures cover a full document
(title, language, meta tags, charset, base reference, links, images, scripts), a fragment without a head,
an empty head, UTF-8 text, a legacy Windows-1252 document with its declared charset, and deeply nested
markup.

Coverage includes every head field; omitted fields when absent; structure counts; bounded excerpts; zero
counts for a script-only document; section-opening headings kept in the text; list aggregation without
duplication; UTF-8 and legacy decoding; invalid binary and empty sources; oversized and cancelled
sources; schema conformance of every returned value; source bytes unchanged; the read budget charged
exactly once; host provider selection, budget and failure isolation; and assembly discovery without a
manifest.

The preview capability is covered by `HtmlPreviewPluginTests`: descriptor claims and schema identity,
a real decoded PNG at the configured width, empty and script-only documents producing no artifact, the
parsed lines (headings flagged) handed to a stub renderer, an unavailable renderer producing nothing, an
oversized source charged nothing, and cancellation before the first read. The host integration tests
assert that a document produces both the metadata record and the preview record with one `image/png`
artifact, and that a textless document still has its metadata but no preview artifacts.
