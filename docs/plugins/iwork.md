# Apple iWork document metadata plugin

Implements #739 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.IWork/` → plugin id `SkyCD.Plugin.IWork`.
Tests: `tests/SkyCD.Plugin.iWork.Tests/`.

This plugin is deliberately independent of the PDF, image and video extraction: it claims only the three
modern iWork types, so a document is never also routed through another plugin's reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document metadata and package inventory | `IWorkMetadataPlugin : IMediaMetadataCapability` | `skycd.iwork.metadata` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a test
asserts none exists.

## Supported file types

| Extension | Content type | Document type |
|---|---|---|
| `.pages` | `application/x-iwork-pages-sffpages` | `pages` |
| `.numbers` | `application/x-iwork-numbers-sffnumbers` | `numbers` |
| `.key` | `application/x-iwork-keynote-sffkey` | `keynote` |

Only the **modern** iWork package format is supported — the ZIP container used since iWork 2013. Both shipped
layouts are read: a flat package that stores the component archive directly as `Index/*.iwa`, and a bundle
that keeps the same archive as a nested `Index.zip`. Legacy iWork '09 documents keep their index in a
root `index.xml` or `index.apxl`; they are intact but a different generation, and raise
`IWorkUnsupportedFormatException` rather than being partially catalogued.

When a source's extension and content type do not identify the document, the type is inferred from the
component inventory: slide components imply Keynote, table components imply Numbers.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | Always `iwork` |
| `DocumentType` | Text | – | `pages`, `numbers` or `keynote` |
| `IsPasswordProtected` | Boolean | – | Package carries an iWork password verifier |
| `FormatVersion` | Text | – | iWork file format version, e.g. `14.4` |
| `ProducerBuild` | Text | – | Latest producing application build, e.g. `Pages 14.4` |
| `DocumentIdentifier` | Text | – | Document UUID in canonical form |
| `Language` | Text | – | Document language code |
| `Locale` | Text | – | Document locale |
| `IsMultiPage` | Boolean | – | Document declares multi-page content |
| `HasExternalData` | Boolean | – | Document references missing or unmaterialised external data |
| `HasPreview` | Boolean | – | Package carries a root-level preview image |
| `PreviewWidth` / `PreviewHeight` | Integer | `px` | Dimensions read from the preview header |
| `DataAssetCount` | Integer | – | Entries under `Data/` |
| `IwaComponentCount` | Integer | – | Component streams in the index |
| `SlideCount` | Integer | – | Keynote slides, excluding templates |

A field the package does not carry is **omitted**, never published as an empty string or zero, so "unknown"
stays distinguishable from "none". The always-present fields are the container marker, the protection flag and
the inventory counts; everything else depends on what the package actually contains.

`IwaComponentCount` and `DataAssetCount` describe the package inventory, not its visible content: a component
stream may belong to a template, a table or any other object the format defines. `SlideCount` is the exception
that is given a precise meaning, because Keynote slide components are named unambiguously.

## Password-protected documents

A protected iWork package is still a readable ZIP: protection encrypts the member payloads, not the container.
Two members stay plain — `Metadata/Properties.plist` and `Metadata/DocumentIdentifier` — so a protected
document is catalogued normally with `IsPasswordProtected = true`, its format version, language, locale and
identifier available, and the document type and inventory still reported.

The preview and the build history are encrypted along with the document content, so they are not decoded and
their fields are omitted. **No password is requested, guessed, stored, logged or passed to anything**, and
there is no credential path by design. A protected document never fails extraction merely for being protected.

## No scripts, no external references

Two structural guarantees rather than a policy promise:

- The plugin is handed **bytes through the host's bounded `IMediaSource`, never a path**. A package that
  references another file or a URL has no way to make the plugin fetch it, because the plugin has no
  filesystem or network handle to fetch with.
- The property-list parser ignores the DOCTYPE, resolves no external entity and uses no resolver, so the
  Apple DTD named in a property list is never fetched and a crafted document cannot reach the disk or the
  network. No IWA component stream is decoded at all; the plugin reads only plain members and entry names.

## Resource behaviour

`IWorkSourceReader` reads the source once into a buffer in 256 KiB chunks before any ZIP reader sees it, so the
budget charge equals the document size, once. `MaxSourceBytes` (64 MiB, above the host's 16 MiB default)
rejects oversized sources before a byte is charged, so the host budget is what normally binds.

Every member read is bounded: metadata members are capped at `MaxMetadataBytes` (1 MiB), a nested `Index.zip`
at `MaxIndexBytes` (32 MiB), and a preview at `MaxPreviewHeaderBytes` (64 KiB) because only its header is
needed. A member above its bound is treated as absent, so an oversized or unreadable member costs its fields,
not the whole extraction. Cancellation is checked before the first read, between chunks, before each metadata
member and before each preview candidate.

Damaged input — a non-ZIP source, an empty file, a truncated archive, or a ZIP with no component index and no
legacy index — raises `IWorkDecodeException`, which the host records as a `failed` record with a sanitized
reason. A short read from the host source is also a decode error rather than a partial parse.

## Dependencies and licensing

No package is used. The plugin is built only on the .NET base class library:

| Need | BCL type |
|---|---|
| ZIP container and member access | `System.IO.Compression.ZipArchive` |
| XML property lists | `System.Xml.Linq` with DTD processing disabled |
| PNG/JPEG header sniffing | `ReadOnlySpan<byte>` |

Because nothing is added to the dependency graph, the plugin introduces no licensing question under
`docs/legal/dependency-license-policy.json` and has no native dependency, so it runs on every supported
platform.

## Limitations

- Modern iWork only. Legacy '09 documents (`index.xml` / `index.apxl`) are reported as unsupported.
- The plain package metadata does not carry a title, author, subject or creation/modification date. Those
  values live inside the compressed IWA component streams, which this plugin deliberately does not decode, so
  no such fields are published. There is no OCR and no content excerpt.
- Protected documents expose only their plain metadata; the preview and build history stay unread by design.
- The preview dimensions describe Apple's stored thumbnail, not the document's page size, and only one
  candidate is used (previews are tried in a fixed order).
- `DocumentType` inference covers mislabelled sources, but a package that is genuinely ambiguous (no slides,
  no tables, unknown extension) omits the field rather than guessing.
- Counts are inventory counts. `IwaComponentCount` is not a count of pages, sheets or slides; `SlideCount`
  is keynote-only.

## Testing

`tests/SkyCD.Plugin.iWork.Tests/` builds **structurally valid iWork packages in memory** — real ZIP
containers, XML property lists, PNG and JPEG preview headers — so the suite commits no binary blobs and every
expected value is known to the test asserting it. Fixtures cover flat and bundle layouts, Keynote slide and
Numbers table inventories, a full property list, an identifier entry and a property-list fallback, a
protected package with opaque encrypted members, and missing members.

Coverage includes the descriptor; every metadata field; document type from extension, content type and
inventory; slide counting that excludes templates; bundle reading through `Index.zip`; omitted fields when
members are absent; identifier canonicalisation; producer build selection past template entries; PNG and JPEG
preview dimensions; protected packages publishing plain metadata with no preview fields; legacy, missing-index,
non-ZIP, truncated and empty sources; oversized rejection with zero bytes charged; cancellation with zero
bytes charged; schema conformance of every returned value; source bytes unchanged; the read budget charged
exactly once; assembly discovery and the absence of `plugin.json`; the host's own source budget and failure
isolation; and offline retrieval through the real scheduler and Couchbase Lite store with the database
reopened and the plugin unloaded.
