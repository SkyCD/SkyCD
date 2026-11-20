# OpenOffice document metadata plugin

Implements #734 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.OpenOffice/` → plugin id `SkyCD.Plugin.OpenOffice`.
Tests: `tests/SkyCD.Plugin.OpenOffice.Tests/`.

This plugin is deliberately independent of the PDF, RTF and WordPerfect plugins: it claims only OpenDocument
and legacy OpenOffice.org extensions and MIME types, so an OpenOffice package is never also routed through
another metadata reader.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document metadata | `OpenOfficeMetadataPlugin : IMediaMetadataCapability` | `skycd.openoffice.metadata` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a test
asserts none exists.

## Supported files

| Source | Support |
|---|---|
| OpenDocument 1.0–1.3 text, spreadsheet, presentation, graphics, formula, chart, image and database packages | Declared MIME type, `office:version` and the `office:meta` fields |
| OpenDocument templates, master documents and web documents (`.ott`, `.ots`, `.otp`, `.otg`, `.otm`, `.odm`, `.oth`) | Same metadata path, with `DocumentType` reporting the template or master variant |
| Legacy OpenOffice.org 1.x packages (`.sxw`, `.stw`, `.sxg`, `.sxc`, `.stc`, `.sxi`, `.sti`, `.sxd`, `.std`, `.sxm`) | Same metadata path through the `http://openoffice.org/2000/` namespace family |
| OpenDocument formula templates (`.otf`) and chart templates (`.otc`) | Selected by MIME type only: the `.otf` suffix belongs to OpenType fonts and `.otc` to OpenType collections, both already claimed by the Fonts plugin |
| Any other format | Not matched by extension or content type; the plugin also rejects a source that is not a ZIP package |

Recognition is structural: the source must be a ZIP package. The declared `mimetype` member supplies the
container family and document type; when it is missing, the filename extension is used. A package that is
mislabelled on disk but carries an OpenDocument `meta.xml` still yields its metadata.

`Container` is `odf` for `application/vnd.oasis.opendocument.*` packages and `openoffice` for
`application/vnd.sun.xml.*` packages. `DocumentType` reports one of `text`, `text-template`, `text-master`,
`text-master-template`, `text-web`, `spreadsheet`, `spreadsheet-template`, `presentation`,
`presentation-template`, `graphics`, `graphics-template`, `formula`, `formula-template`, `chart`,
`chart-template`, `image`, `image-template` or `database`. `FormatVersion` is the root's `office:version`
attribute when present.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | `odf` or `openoffice` |
| `DocumentType` | Text | – | Declared document kind, such as `text` or `spreadsheet` |
| `FormatVersion` | Text | – | Root `office:version`, such as `1.2` or `1.0` |
| `IsEncrypted` | Boolean | – | True when the manifest declares encrypted content |
| `Title`, `Subject`, `Description` | Text | – | `dc:title`, `dc:subject`, `dc:description` |
| `Author`, `InitialCreator` | Text | – | `dc:creator` and `meta:initial-creator` |
| `Keywords` | Text | – | All `meta:keyword` values, joined in document order with `, ` |
| `Generator`, `PrintedBy` | Text | – | `meta:generator` and `meta:printed-by` |
| `Language`, `Rights` | Text | – | `dc:language` and `dc:rights` |
| `Publisher`, `Contributor` | Text | – | `dc:publisher` and `dc:contributor` |
| `Identifier`, `Source`, `Relation`, `Coverage` | Text | – | The Dublin Core fields of the same name |
| `Type`, `Format` | Text | – | `dc:type` and `dc:format` |
| `Template` | Text | – | The template's own name, not its location |
| `CreatedAt`, `ModifiedAt`, `PrintedAt` | Timestamp | – | `meta:creation-date`, `dc:date`, `meta:print-date` |
| `EditingCycles` | Integer | – | `meta:editing-cycles` |
| `EditingTimeMinutes` | Integer | `min` | `meta:editing-duration`, truncated to whole minutes |
| `PageCount`, `TableCount`, `ImageCount`, `ObjectCount` | Integer | – | The matching `meta:document-statistic` attributes |
| `ParagraphCount`, `WordCount`, `CharacterCount` | Integer | – | The matching `meta:document-statistic` attributes |
| `CellCount`, `SheetCount` | Integer | – | The matching `meta:document-statistic` attributes |

A field the package does not carry is **omitted**, never published as an empty string, so "unknown" stays
distinguishable from "none". The first non-blank value for a repeated element wins. Only an element's own
text is used, so the nested `meta:date` inside a `meta:template` element is not mistaken for the template
name. Statistics must be non-negative integers; zero is a real value and is published.

Dates are XML Schema `dateTime` values. A value with a UTC offset keeps that offset; a value without one is
published as a nominal time with a zero offset rather than being shifted by the machine's current time zone,
so the same file always yields the same value. A date or duration that cannot be parsed is omitted.

## Missing, damaged and protected packages

A readable package without `meta.xml` still publishes `Container`, `DocumentType` and `IsEncrypted`, so a
package whose metadata was stripped is catalogued rather than lost. A metadata member that is empty, over the
configured bound or unreadable as a ZIP member is treated the same way.

A source that is not a ZIP package, a damaged ZIP container, and a `meta.xml` that is not well-formed XML or
not an OpenDocument metadata document raise `OpenOfficeDecodeException`, which the host records as a `failed`
record with a sanitized reason. A manifest that cannot be parsed is treated as declaring nothing, and the
metadata member is still attempted.

An encrypted OpenDocument package marks its encrypted members in `META-INF/manifest.xml`. When the manifest
declares encryption and `meta.xml` cannot be read, the plugin raises
`OpenOfficeAuthenticationRequiredException`, distinct from `OpenOfficeDecodeException` because the remedy
differs: the package is intact, it needs a credential. When `meta.xml` is plain despite other encrypted
members, the metadata is published and `IsEncrypted` is true. **No password is requested, guessed, stored,
logged, or used to decrypt anything**, and the plugin does not implement the package encryption algorithms.

## Resource behaviour

`OpenOfficeSourceReader` reads the source once into a buffer in 256 KiB chunks, so the host's budget charge
equals the source size, once; tests assert both the single read and the exact charge. Only three package
members are ever opened: `mimetype`, `META-INF/manifest.xml` and `meta.xml`. Each is read into memory with a
bound checked on the bytes actually decompressed, so a member that declares a small size but expands beyond
the bound is refused instead of materialised. The plugin only ever holds bytes and never a path, so a package
that references another file has no way to make it open that file.

XML parsing disables DTD processing, supplies no resolver and caps the characters in the document, so a
DOCTYPE, an external entity or a small highly compressed member cannot pull in a file or expand without limit.

`MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources before a byte is charged.
`MaxMetadataBytes` (1 MiB) bounds each XML member. An empty source, cancellation before the first read and
cancellation after reading all charge only what was actually read. Cancellation is checked before the first
read, between chunks and between parsing steps.

## Dependencies and licensing

This plugin has **no NuGet dependencies**. The metadata parser, the package reader and the source reader are
implemented in the project on top of the .NET 10 runtime libraries `System.IO.Compression` and
`System.Xml.Linq` (MIT-licensed .NET runtime). Running `dotnet list` with `package --include-transitive` on
the plugin project reports no packages, so the blocked-license, unknown-license and explicit-whitelist rules in
`docs/legal/dependency-license-policy.json` are unchanged and no policy file change is made.

| Candidate | Version | License | Decision |
|---|---|---|---|
| ODF Toolkit (Apache) | – | Apache-2.0 | Rejected. A Java library: not consumable from .NET 10. |
| AODL | – | LGPL-2.1 | Rejected. The checked-in policy blocks the LGPL family, and the library is unmaintained. |
| Open XML SDK | – | MIT | Rejected. Reads OOXML, not OpenDocument packages. |
| Aspose.Words / GroupDocs.Parser | – | Proprietary EULA | Rejected. Both support OpenDocument metadata, but the EULA is not policy-approved and the first-party parser is small and sufficient. |
| SharpZipLib | 1.4.2 | MIT | Rejected. Adds nothing over `System.IO.Compression` for reading a few members. |

The format facts come from the OASIS OpenDocument specification and the OpenOffice.org 1.x XML format
reference. No third-party code is linked, bundled or copied.

## Limitations

- Only `meta.xml` is read. Document text, styles, embedded objects, thumbnails and the document body are out of
  scope; this plugin does not render or extract content.
- Only the nine `meta:document-statistic` attributes listed above are published. Other statistics and
  `meta:user-defined` fields are not, because the schema is a fixed cached-data contract.
- Formula and chart templates are selected by MIME type only, because their `.otf` and `.otc` suffixes belong
  to fonts and OpenType collections.
- The `Template` field publishes the template's display name; the `xlink:href` location is not published.
- OpenOffice.org 1.x packages expose the same `office:meta` subset; anything that generation stores outside it
  is not read.
- Protected packages are never decrypted. There is no credential path by design.
- Dates without a stored offset are nominal zero-offset values, as described above.

## Testing

`tests/SkyCD.Plugin.OpenOffice.Tests/` builds **structurally valid ZIP packages in memory**, so the suite
commits no binary blobs and every expected value is known to the test asserting it. Fixtures cover the
OpenDocument 1.2 namespaces, the legacy `http://openoffice.org/2000/` namespaces, a package without metadata,
a package without a declared MIME type, plain and encrypted manifests, truncated and malformed metadata, a
wrong metadata root, an external entity, a non-ZIP source, a truncated package, and an oversized metadata
member.

Coverage includes the descriptor and schema, every published field, keyword joining, first-non-blank
precedence, omission of blank, invalid and negative values, offset and offsetless dates, whole-minute
durations, zero statistics, container and document-type resolution from MIME type and extension, encrypted
and damaged classification, empty and oversized inputs with zero bytes charged, cancellation, the exact
budget charge and read count, the host's extension and content-type matching, standard and opt-in selection,
failure classification, assembly discovery without a manifest, and storage through the real scheduler and
in-memory cache store.
