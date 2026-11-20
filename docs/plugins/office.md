# Microsoft Office document metadata plugin

Implements #736 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Office/` → plugin id `SkyCD.Plugin.Office`.
Tests: `tests/SkyCD.Plugin.Office.Tests/`.

The plugin claims the Word, Excel and PowerPoint filename extensions and MIME types, so an Office document
is never routed through another metadata reader. It follows the PDF and Works plugins' conventions: a
versioned schema, a bounded single-pass source read, omission instead of empty values, and a sanitized
decode failure. It does not need Microsoft Office, an Office Open XML SDK, or a platform-specific library.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document metadata | `OfficeMetadataPlugin : IMediaMetadataCapability` | `skycd.office.metadata` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

## Supported files

| Source | Recognition | Support |
|---|---|---|
| OOXML Word: `.docx`, `.docm`, `.dotx`, `.dotm` | ZIP package with `word/document.xml` | `docProps/core.xml` and `docProps/app.xml` |
| OOXML Excel: `.xlsx`, `.xlsm`, `.xltx`, `.xltm`, `.xlsb` | ZIP package with `xl/workbook.xml` or `xl/workbook.bin` | `docProps/core.xml` and `docProps/app.xml` |
| OOXML PowerPoint: `.pptx`, `.pptm`, `.potx`, `.potm`, `.ppsx`, `.ppsm` | ZIP package with `ppt/presentation.xml` | `docProps/core.xml` and `docProps/app.xml` |
| Legacy Word: `.doc` | OLE2 compound file with a `WordDocument` stream | `\x05SummaryInformation` and `\x05DocumentSummaryInformation` |
| Legacy Excel: `.xls` | OLE2 compound file with a `Workbook` or `Book` stream | Same two summary streams |
| Legacy PowerPoint: `.ppt` | OLE2 compound file with a `PowerPoint Document` stream | Same two summary streams |
| Anything else | Rejected by structure | A decode failure |

Recognition is structural, not extension-based. A ZIP whose document part is missing is rejected rather than
published as an Office document, and a compound file with no Word, Excel or PowerPoint stream is rejected the
same way. Extension and MIME matching only decide whether the host offers the plugin.

The MIME types include the transitional and macro-enabled variants plus the legacy
`application/msword`, `application/vnd.ms-excel` and `application/vnd.ms-powerpoint`, so a content-type match
selects the plugin even when the filename has an unusual extension. In that case `DocumentType` still comes
from the package or compound contents.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `Container` | Text | Always `ooxml` for a ZIP package or `ole` for a compound file |
| `DocumentType` | Text | `word`, `excel` or `powerpoint`, taken from the document part or stream |
| `IsEncrypted` | Boolean | Published only when an encryption wrapper is present; absent otherwise |
| `Title`, `Subject`, `Author`, `Keywords`, `Comments` | Text | Core document properties |
| `Category`, `ContentStatus`, `LastSavedBy`, `RevisionNumber`, `Language` | Text | Core document properties |
| `Template` | Text | `app.xml` template or the legacy summary template field |
| `ApplicationName`, `ApplicationVersion`, `Company`, `Manager` | Text | Writing application and organisation |
| `CreatedAt`, `ModifiedAt`, `LastPrintedAt` | Timestamp | Document dates, normalized to UTC |
| `PageCount`, `WordCount`, `CharacterCount` | Integer | Application statistics |
| `SlideCount` | Integer | Presentation slide count from `app.xml` |

A field the document does not carry is **omitted**, never published as an empty string or zero, so "unknown"
stays distinguishable from "none". Statistics are published only when greater than zero, because Office
writers store zero for "not computed" as often as for "empty". `IsEncrypted` is never published as `false`:
for a legacy compound document, encryption is not uniformly detectable without decrypting, and claiming
"not encrypted" would overstate what was verified.

## OOXML package reading

Only `docProps/core.xml` and `docProps/app.xml` are decompressed from the package. A part larger than
`OfficePluginOptions.MaxMetadataPartBytes` (4 MiB) is treated as absent, and decompression stops at the cap
even when the ZIP entry declares a smaller size, so a crafted package cannot make the plugin hold more than
the cap.

Both parts are parsed with `XmlReader` and `XDocument`. A DTD is prohibited and no external resource is
resolved, so a part cannot expand entities or fetch anything. Elements are matched by **local name among the
direct children of the root element**, which tolerates transitional and strict namespaces as well as
non-standard prefixes while ignoring nested vector fields such as `HeadingPairs` and `TitlesOfParts`.

A damaged or unparsable part is skipped: the document keeps the metadata from the other part and from the
container facts. A damaged ZIP container, a package with no Office document part, or a source that is neither
a ZIP package nor a compound file raises `OfficeDecodeException`, which the host records as a `failed`
record with a sanitized reason.

`dcterms:created`, `dcterms:modified` and `cp:lastPrinted` are W3C date-time values. A value without a zone
is taken as UTC. `cp:revision` stays text because writers store it as a string as often as an integer.

## Legacy compound document reading

Office documents predating OOXML wrap their content in an OLE2 compound file. `CompoundFile` implements only
the read path for named streams: header, DIFAT, FAT, directory, the regular sector chain and the mini stream
used for streams below 4096 bytes. Every declared sector, stream size and chain link is checked against the
bytes actually present, chain traversal is bounded by the sector count, and a loop or a pointer outside the
file is rejected instead of followed. `OfficePropertySet` then reads the first section of each summary
property set: codepage-selected `VT_LPSTR`, UTF-16 `VT_LPWSTR`/`VT_BSTR`, `VT_I2`/`VT_UI2`/`VT_I4`/`VT_UI4`
numbers and `VT_FILETIME` dates. Vector variants are not published.

Both readers are lenient only where the data is optional. A damaged summary stream costs its fields while
the document still publishes `Container` and `DocumentType`. A damaged container is a decode failure.

The readers are in-tree rather than a package because the usual .NET compound-file library is MPL-2.0, which
the repository's BSD-2-Clause outbound policy does not accept (see [Dependencies and licensing](#dependencies-and-licensing)).
They are deliberately kept inside the plugin assemblies, matching the Microsoft Works and WordPerfect
plugins; a shared OLE assembly would add a project and deployment surface for code that is only read.

## Encryption

Office 2007 and later protect an OOXML package by wrapping it, byte for byte, in a compound file with
`EncryptionInfo` and `EncryptedPackage` streams. The plugin detects that wrapper, publishes
`Container=ooxml` and `IsEncrypted=true`, and reads nothing from it. The payload is **never decrypted**, and
no credential is requested, guessed, stored or logged. `DocumentType` is absent in that case because the
package contents cannot be inspected without the key.

Legacy Word, Excel and PowerPoint documents can store an encrypted payload while leaving the summary
property streams in plain text. The plugin reads those plain summary values, which is why a protected legacy
document can still publish its metadata; it never attempts to read the encrypted body. Where a legacy writer
encrypts the summary as well, the metadata is simply absent.

## Resource behaviour

`OfficeSourceReader` reads the source once into a buffer in 256 KiB chunks, so the host's budget charge equals
the source size, exactly once. The plugin only ever holds bytes and never a path, so a document that
references another file has no way to make it open that file. Packages are opened from the in-memory bytes
with `ZipArchive`, so no temporary file is created.

`OfficePluginOptions.MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources
before a byte is charged, so the host budget is what normally binds. `MaxMetadataPartBytes` (4 MiB) bounds
each metadata part. An empty source fails with `OfficeDecodeException`; cancellation is checked before the
first read, between chunks, between parts and after parsing, and a cancelled extraction publishes nothing.

## Dependencies and licensing

This plugin has **no NuGet dependencies**. It uses `System.IO.Compression.ZipArchive` and
`System.Xml`/`System.Xml.Linq` from the .NET 10 runtime, plus the existing plugin abstraction project. The
property set parser, the compound-file reader and the source reader are implemented in the project.

| Candidate | Version | License | Decision |
|---|---|---|---|
| `DocumentFormat.OpenXml` | 3.5.1 | MIT | Rejected. Maintained and `.NET 10`-compatible, but it is an object model for the whole package; reading two `docProps` parts does not need it, and it adds a large dependency and API surface to audit. |
| `NPOI` | 2.8.1 | Apache-2.0 | Rejected. Supports the legacy formats but is a full spreadsheet/document library; a metadata-only reader becomes a fraction of the dependency and carries no native code. |
| `OpenMcdf` | 3.4.0 | MPL-2.0 | Rejected. Maintained and `.NET 10`-targeting, but the checked-in OSADL BSD-2-Clause outbound row marks MPL-2.0 incompatible, so the CI license gate rejects it without a policy change. |
| `Bodu.IO.Compound` | 1.3.0 | MIT | Rejected. Reads CFB and targets `.NET 10`, but the package is new, has one maintainer and under 3,000 total downloads, so it adds supply-chain risk that the small read-only reader avoids. |

No policy file change is made.

## Limitations

- Only the standard document properties are read. Document text, styles, embedded objects, spreadsheets cells,
  presentation slide text, custom XML parts and user-defined `docProps/custom.xml` properties are out of scope;
  this plugin does not render or extract content.
- `docProps/custom.xml` is not published, because the schema is a fixed cached-data contract.
- Legacy `.doc`, `.xls` and `.ppt` streams are recognized by their standard stream names. A damaged or
  unusually written container may therefore be rejected even if an Office application can repair it.
- `ApplicationVersion` is the writer's own string (for example `16.0000`), not a parsed version number.
- `SlideCount`, `PageCount`, `WordCount` and `CharacterCount` are whatever the writer recorded; Office does
  not recompute them when a document is saved by another application.
- Encrypted OOXML packages are identified but not opened, so they publish no document properties.
- There is no credential path by design. Password-protected content is never decrypted.

## Testing

`tests/SkyCD.Plugin.Office.Tests/` builds **structurally valid OOXML packages and OLE2 compound documents in
memory**, so the suite commits no binary blobs and every expected value is known to the test asserting it.
Fixtures cover Word, Excel (including the binary workbook part) and PowerPoint packages, complete and partial
core and application parts, case-varied part names, nested vector fields, DTD-bearing parts, encrypted
wrappers, legacy Word/Excel/PowerPoint containers, missing and damaged parts, empty and non-Office sources,
and damaged containers.

Coverage includes the descriptor and schema, every published field, omission of absent, blank and zero values,
whitespace folding, date parsing, integer parsing, every damage path, truncation and header checks, the exact
budget charge and read count, cancellation, progress reporting, the host's extension and content type
matching, failure classification, assembly discovery without a manifest, and offline storage through the real
scheduler and in-memory cache store.
