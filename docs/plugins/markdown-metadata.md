# Markdown document metadata plugin

Implements #738 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Markdown.Metadata/` → plugin id `SkyCD.Plugin.Markdown.Metadata`.
Tests: `tests/SkyCD.Plugin.Markdown.Metadata.Tests/`.

This plugin is separate from `SkyCD.Plugin.Markdown`, which writes catalog exports and is write-only for
reading. The metadata plugin claims only `.md`, `.markdown`, `.mdown` and `.mkd`
(`text/markdown`, `text/x-markdown`), so a Markdown file is never also routed through the plain-text reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Metadata, structure and bounded text | `MarkdownMetadataPlugin : IMediaMetadataCapability` | `skycd.markdown.metadata` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `Container` | Text | Always `markdown` |
| `HasFrontMatter` | Boolean | A fenced YAML block opens the document |
| `FrontMatterValid` | Boolean | Present only when `HasFrontMatter`; false when the YAML or its size is rejected |
| `Title`, `Subject`, `Author`, `Keywords`, `Category`, `Language` | Text | Front matter values |
| `PublishedAt` | Timestamp | Front matter `date` / `published` / `created`, parsed as ISO-8601-ish text |
| `FirstHeading` | Text | Visible text of the first heading, at any level |
| `HeadingCount`, `LinkCount`, `ImageCount`, `CodeBlockCount` | Integer | Structure counted from the parsed document |
| `WordCount`, `CharacterCount` | Integer | Computed from the rendered plain text |
| `TextExcerpt` | Text | Bounded plain-text excerpt of the body |
| `TextExcerptTruncated` | Boolean | Whether the excerpt was cut short |

A field the document does not carry is **omitted**, never published as an empty string, so "unknown" stays
distinguishable from "none". The computed counts are always published, because zero headings or zero words
is a fact about the document rather than missing metadata. A textless document has zero counts and no
`TextExcerpt`/`TextExcerptTruncated` at all.

## Front matter

A block counts as front matter only when it opens with a `---` line **and** a later `---` or `...` line
closes it. An opener with no closer is a thematic break in Markdown, so the whole source stays in the body.

Top-level YAML keys are matched case-insensitively:

| Field | Accepted keys |
|---|---|
| `Title` | `title` |
| `Subject` | `subject`, `description`, `summary` |
| `Author` | `author`, `authors`, `creator` |
| `Keywords` | `keywords`, `tags` |
| `Category` | `category`, `categories` |
| `Language` | `language`, `lang` |
| `PublishedAt` | `date`, `published`, `created` |

Scalar values become one string; sequences are joined with `, ` (up to 32 items per field). Nested
mappings are skipped rather than flattened into an ambiguous string. Each value is bounded by
`MaxMetadataValueCharacters` (2048), and front matter larger than `MaxFrontMatterCharacters` (64 KiB) is
treated as damaged rather than parsed.

Damaged YAML does not fail the extraction: `FrontMatterValid` is false, front matter fields are omitted,
and the body is still catalogued. That keeps a malformed metadata block from costing the document itself.

## Structure and text

The body is parsed with Markdig's CommonMark-plus-extensions pipeline. Counts come from the syntax tree, so
headings inside block quotes or lists count the same as top-level ones. The excerpt and the word/character
counts come from the rendered plain text: markup, link targets and image URLs are not text, while link
labels and code block contents are.

The excerpt is bounded by characters (`MaxTextExcerptCharacters`, 4096). `TextExcerptTruncated` records
whether the bound cut the text short, because truncation changes what search can find: a term past the bound
is genuinely absent from the stored data, not absent from the document.

## No scripts, no external references

Two structural guarantees rather than a policy promise:

- The plugin is handed **bytes through the host's bounded `IMediaSource`, never a path**. A document that
  links to another file or a URL has no way to make the plugin fetch it, because the plugin has no
  filesystem or network handle to fetch with. Links become counts and labels, nothing more.
- Nothing in the pipeline executes code. HTML or JavaScript embedded in a Markdown file is treated as text;
  no HTML is rendered and no script host is involved.

## Protected documents

Markdown has no encryption or password mechanism of its own. A file that is actually protected by an
outer container is not detected as Markdown at all: the bytes are not valid UTF-8/UTF-16 text, and the
plugin reports `MarkdownDecodeException` rather than guessing or decrypting anything. No password is ever
requested, guessed, stored or logged.

## Resource behaviour

`MarkdownSourceReader` reads the source once into a buffer in 256 KiB chunks before the parser sees it.
UTF-8 is the default; a UTF-16 byte-order mark selects UTF-16. Invalid byte sequences and embedded NUL bytes
are rejected as `MarkdownDecodeException`, so a binary file with an `.md` extension does not produce
mojibake metadata. `MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources
before a byte is charged, so the host budget normally binds. Cancellation is checked before the first read,
after parsing and before the excerpt is assembled.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `Markdig` | 1.6.0 | BSD-2-Clause | CommonMark/GFM parsing, syntax tree and plain-text rendering |
| `YamlDotNet` | 18.1.0 | MIT | Safe representation-model reading of the front matter block |

Both are on the allow-list in `docs/legal/dependency-license-policy.json`. YamlDotNet 18.1.0 is already
used by the YAML plugin and the UI; Markdig is new, has no dependencies on `net10.0`, and is actively
maintained.

## Limitations

- Markdown only. MDX, AsciiDoc, reStructuredText and other markup formats are out of scope.
- Front matter fields are read from top-level keys only. Nested values and anchors are not flattened.
- `PublishedAt` reflects the front matter's own date, not a filesystem timestamp, and is omitted when the
  value is not parseable.
- Word and character counts describe the rendered plain text, not the raw source, so markup syntax is not
  counted.
- There is no thumbnail: Markdown has no fixed page geometry to rasterize, and this issue's scope is
  metadata collection.
- A Markdown file with no front matter has no `FrontMatterValid` value at all; the absence of that field is
  the "no front matter" state, while `HasFrontMatter = false` states it explicitly.

## Testing

`tests/SkyCD.Plugin.Markdown.Metadata.Tests/` builds Markdown sources in memory. Fixtures cover a full
front matter block with lists and a date, a document without front matter, damaged YAML, an unterminated
delimiter, oversized and nested front matter, structure counts, markup-to-plain-text conversion, bounded
excerpts, textless documents, UTF-16 with a byte-order mark, invalid UTF-8, NUL bytes, oversized and
cancelled sources.

Coverage includes every front matter field and its aliases; omitted fields when absent; damaged front matter
with the body preserved; exact truncation assertion; zero counts for a textless document; schema conformance
of every returned value; source bytes unchanged; the read budget charged exactly once; host provider
selection, budget and failure isolation; and assembly discovery without a manifest.
