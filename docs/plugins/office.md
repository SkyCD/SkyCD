# Microsoft Office document metadata plugin

Implements #736 and the embedded thumbnail preview from #945 on the capability contracts from #435
(`docs/plugins/media-capabilities.md`), and #1241, which made this plugin the single Microsoft Office-family
metadata provider.

Assembly: `Plugins/SkyCD.Plugin.Metadata.Office/` → plugin id `SkyCD.Plugin.Office`.
Tests: `tests/SkyCD.Plugin.Metadata.Office.Tests/`.

The plugin covers Word, Excel, PowerPoint, Visio, OneNote, Access and Project, in their current and legacy
storage forms. Reading is delegated to **OfficeIMO 3.6.2**, one version for every application family, so part
names, relationship maps, property sets and the protected-document refusals come from the library that the rest
of the solution's Office work already uses. The plugin never needs a Microsoft Office installation, never
writes, and never takes a path: it hands the library a stream over the bytes the host supplied. It claims the
filename extensions and MIME types of all seven applications, so an Office document is never routed through
another metadata reader. `.xml` stays **unclaimed** here — it belongs to the XML file-format plugin and the text
metadata plugin — and `.msg` belongs to the Email plugin.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Document metadata | `OfficeMetadataPlugin : IMediaMetadataCapability` | `skycd.office.metadata` v2 |
| Thumbnail preview | `OfficePreviewPlugin : IMediaPreviewCapability` | `skycd.office.preview` v1 |

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

## Schema version 2

Version 2 widens the covered family and adds the count, protection and profile fields the new applications
publish. **Every version 1 key keeps its name, type and meaning**, so a stored version 1 result for a Word,
Excel or PowerPoint document stays comparable with a version 2 one. The version is bumped exactly once, in this
change: the host keys cached results by schema id and version, so one bump is what re-reads the catalog.

## Supported files

| Application | Current forms | Legacy forms |
|---|---|---|
| Word | `.docx`, `.docm`, `.dotx`, `.dotm` | `.doc`, `.dot` |
| Excel | `.xlsx`, `.xlsm`, `.xltx`, `.xltm`, `.xlsb` | `.xls`, `.xlt` |
| PowerPoint | `.pptx`, `.pptm`, `.potx`, `.potm`, `.ppsx`, `.ppsm` | `.ppt`, `.pot`, `.pps` |
| Visio | `.vsdx`, `.vsdm`, `.vstx`, `.vstm`, `.vssx`, `.vssm` | `.vsd`, `.vss`, `.vst` |
| OneNote | `.one` (section), `.onetoc2` (table of contents), `.onepkg` (notebook) | – |
| Access | `.accdb` | `.mdb` |
| Project | `.mpp`, `.mpt` | `.mpx` (delimited interchange text) |

Recognition is structural. A ZIP package is assigned to Word, Excel, PowerPoint or Visio by the top-level part
folder it contains (`word/`, `xl/`, `ppt/`, `visio/`), and a package with none of them is rejected rather than
published as an Office document. A compound file is assigned by the document stream it holds:
`WordDocument`, `Workbook` or `Book`, or `PowerPoint Document`. A compound file that holds none of them and is
not one of the extension-hinted containers below is rejected the same way.

OneNote, Access and Project do not have a container of their own to distinguish them from Word, Excel and
PowerPoint: a notebook section is a revision store, a database is a Jet file, and a plan is either a compound
document or plain text. Those three are therefore resolved from the matched extension first and confirmed
against the leading bytes. Project is the case where the extension cannot decide the container, so the leading
bytes do: `Container` is `ole` for a native plan and `text` for the interchange format.

The MIME types cover the transitional, macro-enabled and legacy forms of every application, plus
`application/onenote`, `application/msonenote`, `application/vnd.ms-access`, `application/x-msaccess` and
`application/vnd.visio`, so a content-type match selects the plugin even when the filename has an unusual
extension. `DocumentType` always comes from the container contents, except for the three families above, where
it follows the confirmed extension hint.

`.mdb` and `application/x-msaccess` are also claimed by the Access and MyCatalogue **file-format** plugins, which
import such a database as a catalog. That is not a competing media claim: those plugins answer "what catalog is
inside this file" and this plugin answers "what describes this file", and no media metadata or preview plugin
claims an Office source.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `Container` | Text | `ooxml` (ZIP package), `ole` (compound file), `onenote` (revision store), `cab` (notebook package), `jet` (Access database) or `text` (Project interchange) |
| `DocumentType` | Text | `word`, `excel`, `powerpoint`, `visio`, `onenote`, `access` or `project` |
| `IsEncrypted` | Boolean | Published only when a protection wrapper or refusal is present; absent otherwise |
| `ProtectionReason` | Text | Why the content was left unread, in the reading library's own words |
| `IsTemplate` | Boolean | Published only for a template, stencil or show: `.dot*`, `.xlt*`, `.pot*`, `.pps*`, `.vst*`, `.vss*`, `.mpt` |
| `Title`, `Subject`, `Author`, `Keywords`, `Comments` | Text | Core document properties |
| `Category`, `ContentStatus`, `LastSavedBy`, `RevisionNumber`, `Language` | Text | Core document properties |
| `Template` | Text | `app.xml` template or the legacy summary template field |
| `ApplicationName`, `ApplicationVersion`, `Company`, `Manager` | Text | Writing application and organisation |
| `CreatedAt`, `ModifiedAt`, `LastPrintedAt` | Timestamp | Document dates, normalized to UTC |
| `PageCount`, `WordCount`, `CharacterCount` | Integer | Application statistics from `app.xml` or the legacy summary |
| `SlideCount` | Integer | Presentation slides, from `app.xml` when the writer recorded one |
| `SheetCount` | Integer | Worksheets in a workbook, or pages in a OneNote section |
| `SectionCount` | Integer | Sections listed by a OneNote table of contents or notebook package |
| `PageTitles` | Text | Page or section titles in document order, newline-separated, capped at 200 titles and 4 KiB |
| `TableCount`, `QueryCount` | Integer | User tables and saved queries in an Access database |
| `TaskCount`, `TopLevelTaskCount` | Integer | All tasks in a plan, and the tasks at the visible first level |
| `PlanStartAt`, `PlanFinishAt` | Timestamp | The schedule dates a project plan is built around |
| `FormatProfile` | Text | The concrete profile the container was read as, such as `docx`, `xlsb`, `drawing` or `ace12` |

A field the document does not carry is **omitted**, never published as an empty string or zero, so "unknown"
stays distinguishable from "none". Statistics are published only when greater than zero, because Office writers
store zero for "not computed" as often as for "empty". `IsEncrypted` and `IsTemplate` are never published as
`false`: for a legacy compound document, encryption is not uniformly detectable without decrypting, and an
absent flag already means "not a template" or "not protected".

## Reading through OfficeIMO

Each family has its own reader that loads the document through the matching library entry point
(`WordDocument`, `ExcelDocument`, `PowerPointDocument`, `VisioDocument`, `OneNoteSection`,
`OneNotePackageReader`/`OneNoteNotebookReader`, `AccessDocument`, `ProjectDocument`) over a
`MemoryStream` on the host's bytes, and reads what the plugin publishes off the resulting model. Every load
option set is built in one place, `OfficeLoadBounds`:

- **Read-only and never persisted.** `DocumentAccessMode.ReadOnly` with
  `DocumentPersistenceMode.Explicit`, so no library path can write the document back.
- **Bounded per package.** `MaxInputBytes` equals the plugin's own source limit, and the package policies add
  caps on package bytes, per-part uncompressed bytes, total uncompressed bytes (8× the compressed source), XML
  characters per part and compression ratio. The library's refusal is what enforces a bound the plugin could not
  otherwise see, because a metadata part is small while a text-heavy document part is not.
- **No external resolution.** Nothing is asked for beyond those bytes: the plugin never receives a path, and a
  document that references another file or an address has no way to make it open it. Workbook external links
  are dropped rather than followed (`PreserveExternalWorkbookLinks = false`).
- **Never a password.** No option is ever given a credential, so a protected document is reported, not opened.

The content policies deliberately stay permissive, unlike the library's own `UntrustedDefaults`. Those reject a
package for *containing* a macro part, an embedded payload or an external relationship, and all three are
ordinary in the formats this plugin must cover: `.docm`, `.xlsm`, `.pptm` and `.vsdm` carry a VBA project, and
any document with a hyperlink carries an external relationship. This plugin only reads properties, so it never
executes a macro, never renders a payload and never opens the target of a relationship. A document is judged by
its size, not by which optional parts it happens to store.

Per application, the model contributes the fields that the property parts do not carry:

| Application | From the model |
|---|---|
| Word, Excel, PowerPoint | `FormatProfile` and the template kind from the source-format descriptor; `SheetCount` from the sheet list; `SlideCount` as a fallback when `app.xml` recorded none |
| Visio | `PageCount`, `Title`, `Author`, the stencil-versus-drawing `FormatProfile`, and `IsTemplate` for a stencil |
| OneNote | Section name, page count and page titles, and the earliest and latest page timestamps for a section; section count and section names for a notebook |
| Access | `TableCount`, `QueryCount`, the Jet `FormatProfile`, and the database caption, author, subject, comments, company and manager from the stored properties |
| Project | Name, author, subject, company, manager, producer version, generation profile, schedule dates, and the total and top-level task counts |

Access is inspected through its catalog only (`DecodeCatalog = true`, `DecodeApplicationObjects = false`) and
loaded read-only: **inspection never executes SQL, VBA or macros**. OneNote reads the table of contents and the
section list without decoding page content (`LoadSectionContent = false`), and skips the recycle bin.

## OOXML property parts

The core and application properties come from the parts the library locates through their relationships, not
from a fixed `docProps/core.xml` path, because producers place the core part differently: OfficeIMO and Word
itself keep it at `package/services/metadata/core-properties/<guid>.psmdcp`.

`OfficePackageCore` reads the core part's own stream with `XmlReader` and `XDocument`, with a DTD prohibited and
no resolver, and matches elements by **local name among the direct children of the root element**. That
tolerates the transitional and strict namespaces and non-standard prefixes, ignores nested vector fields such as
`TitlesOfParts`, and avoids the typed root element, which the SDK leaves unset for a core part reached through a
read-only document — reading it would silently cost every core field. `OfficePackageExtended` reads the
application part through its typed element the same way, by local name.

`dcterms:created`, `dcterms:modified` and `cp:lastPrinted` are W3C date-time values; a value without a zone is
taken as UTC. `cp:revision` stays text because writers store it as a string as often as an integer.

A damaged or unparsable part is skipped: the document keeps the metadata from the other part and from the
container facts. A damaged ZIP container, a package with no Office document part, or a source that is neither a
package nor a compound file raises `OfficeDecodeException`, which the host records as a `failed` record with a
sanitized reason. Any unexpected library failure is wrapped as a decode failure too, so no exception type from a
dependency escapes into the host.

## Legacy compound document reading

Office documents predating OOXML wrap their content in an OLE2 compound file, and their properties live in the
`\x05SummaryInformation` and `\x05DocumentSummaryInformation` property sets. Those two are read by the in-tree
`CompoundFile` and `OfficePropertySet` pair rather than by the library's legacy projection, which converts the
whole binary document into an editable model. A metadata read does not need that conversion: it costs one to two
hundred milliseconds per document, and the projected property bag does not carry the summary fields
(`Template`, `ApplicationName`, pages, words, characters, `ContentStatus`, `Language`) that this schema has
published since version 1. Legacy `.vsd` is covered by the same reader, whose document stream the library models
differently; it publishes the property sets and `DocumentType=visio` from the confirmed hint.

`CompoundFile` implements only the read path for named streams: header, DIFAT, FAT, directory, the regular
sector chain and the mini stream used for streams below 4096 bytes. Every declared sector, stream size and chain
link is checked against the bytes actually present, chain traversal is bounded by the sector count, and a loop or
a pointer outside the file is rejected instead of followed. `OfficePropertySet` reads the first section of each
property set: codepage-selected `VT_LPSTR`, UTF-16 `VT_LPWSTR`/`VT_BSTR`, `VT_I2`/`VT_UI2`/`VT_I4`/`VT_UI4`
numbers and `VT_FILETIME` dates. Vector variants are not published.

Both readers are lenient only where the data is optional. A damaged summary stream costs its fields while the
document still publishes `Container` and `DocumentType`. A damaged container is a decode failure.

The readers are in-tree rather than a package to keep the dependency tree small; the usual .NET compound-file
library is MPL-2.0 (see [Dependencies and licensing](#dependencies-and-licensing)). They stay inside the plugin
assembly, matching the Microsoft Works and WordPerfect plugins; a shared OLE assembly would add a project and
deployment surface for code that is only read.

## Encryption and protection

Office 2007 and later protect an OOXML package by wrapping it, byte for byte, in a compound file with
`EncryptionInfo` and `EncryptedPackage` streams. The plugin detects that wrapper from the directory names,
publishes `Container=ooxml`, `IsEncrypted=true` and a `ProtectionReason`, and reads nothing from it.

Where the container is readable but the library refuses the content, the refusal itself is the answer: the
package security exception, the OneNote encrypted-revision diagnostic, and the Access and Project protection
diagnostics are all published as `IsEncrypted=true` with the library's own message, folded and shortened to 240
characters, as `ProtectionReason`. A section whose current revision is encrypted is reported as protected and
**never** falls back to an older plaintext revision. A document that opens and merely records that its author
preferred a password is an ordinary document: it publishes its fields and no `IsEncrypted`.

The payload is **never decrypted**, and no credential is requested, guessed, stored or logged. Legacy Word,
Excel and PowerPoint documents can store an encrypted body while leaving the summary property sets in plain
text; the plugin reads those plain values, which is why a protected legacy document can still publish its
metadata, and never attempts to read the encrypted body. Where a legacy writer encrypts the summary as well, the
metadata is simply absent.

## Thumbnail preview

The preview capability copies a picture the document already embeds; it renders nothing, so a scan never pays
for a rasterizer. Schema `skycd.office.preview` version 1 carries identity only, and the descriptor claims the
same extensions and MIME types as the metadata capability.

- **OOXML packages:** the standard `docProps/thumbnail.jpeg` part is decompressed under the preview byte cap and
  published as one `image/jpeg` artifact. The JPEG frame header is parsed first, and a part that is not a JPEG or
  whose edges exceed `OfficePluginOptions.MaxThumbnailDimension` (1024 px) is treated as absent.
- **Legacy compound documents:** the thumbnail is the `VT_CF` clipboard property (id 17) of
  `\x05SummaryInformation`. Only the device-independent bitmap forms (`CF_DIB` and `CF_DIBV5`) are published; the
  packed DIB gains a 14-byte bitmap file header and is published as `image/bmp`, which the host decodes natively.
  Metafile clipboard formats stay unrendered because the plugin adds no rasterizer.
- **Containers with no picture:** a OneNote notebook package, an Access database and a Project plan hold no
  embedded thumbnail at all, so they return an empty artifact list rather than an error. A missing thumbnail, an
  encrypted package, an unreadable part, an unsupported clipboard format and a part above `MaxPreviewBytes`
  (4 MiB) all end the same way, which the host records as "no preview". A damaged ZIP container is a decode
  failure, exactly as for metadata, and a preview failure never discards the metadata capability's result.

## Resource behaviour

`OfficeSourceReader` reads the source once into a buffer in 256 KiB chunks, so the host's budget charge equals
the source size, exactly once. The plugin only ever holds bytes and never a path, so no library overload that
takes a file name is reachable. Packages are opened from the in-memory bytes, so no temporary file is created.

`OfficePluginOptions.MaxSourceBytes` (64 MiB, above the host's 16 MiB default) rejects oversized sources before a
byte is charged, so the host budget is what normally binds. `MaxMetadataPartBytes` (4 MiB) bounds the summary
stream a legacy thumbnail is read from, and `MaxPreviewBytes` (4 MiB) bounds the embedded thumbnail. An empty
source fails with `OfficeDecodeException`; cancellation is checked before the first read, between chunks, between
parts and after parsing, and a cancelled extraction publishes nothing.

## Scan cost

The plugin runs during browsing scans, so open cost was measured per family with the Release build on Windows
x64, seven runs per fixture, reporting the median. The worst column is the first read in the process, which
pays library warm-up.

| Source | Size | Median | Worst |
|---|---|---|---|
| `.docx` with full properties | 12 KiB | 1 ms | 51 ms |
| `.xlsx` | 5.5 KiB | 0 ms | 19 ms |
| `.pptx`, 12 slides | 34 KiB | 3 ms | 15 ms |
| `.vsdx`, 6 pages | 13 KiB | 1 ms | 38 ms |
| `.one`, 3 pages | 6.6 KiB | 0 ms | 5 ms |
| `.onepkg` | 5.2 KiB | 0 ms | 1 ms |
| `.accdb`, 12 tables | 280 KiB | 0 ms | 32 ms |
| `.mdb`, 12 tables | 280 KiB | 0 ms | 4 ms |
| `.mpp`, 12 tasks | 102 KiB | 3 ms | 34 ms |
| `.mpx` | 2 KiB | 0 ms | 17 ms |
| legacy `.doc` / `.xls` / `.ppt` | 2–3 KiB | 0 ms | 5 ms |
| `.docx`, 20 000 paragraphs | 70 KiB | 292 ms | 419 ms |
| `.docx`, 60 000 paragraphs | 187 KiB | 479 ms | 534 ms |
| `.xlsx`, 200 sheets | 71 KiB | 10 ms | 110 ms |
| `.pptx`, 400 slides | 302 KiB | 38 ms | 60 ms |
| `.pptx`, 2000 slides | 1.4 MiB | 224 ms | 379 ms |

Ordinary documents, including the legacy and database families, cost a few milliseconds. The Word path is the
one that scales: `WordDocument.Load` parses the document body, so a text-heavy document costs roughly 1.5 ms per
1000 paragraphs regardless of how small the compressed file is. The host's 16 MiB source budget and its per-item
timeout are what bound that, and the bounds handed to the library cap the work rather than let a package expand
without limit.

## Dependencies and licensing

Direct dependencies, all **OfficeIMO 3.6.2** (MIT), pinned in
`Plugins/SkyCD.Plugin.Metadata.Office/packages.lock.json` at the same version the HTML metadata and IWork
plugins use: `OfficeIMO.Word`, `OfficeIMO.Excel`, `OfficeIMO.PowerPoint`, `OfficeIMO.Visio`,
`OfficeIMO.OneNote`, `OfficeIMO.Access`, `OfficeIMO.Project`. The transitive closure adds `OfficeIMO.Core`
3.6.2 (MIT), `DocumentFormat.OpenXml` and `DocumentFormat.OpenXml.Framework` 3.5.1 (MIT) and
`System.IO.Packaging` 10.0.8 (MIT). Every one is Permissive and passes the Trivy gate in
`docs/legal/dependency-policy.md`, and the same MIT closure the HTML, IWork, OpenDocument, RTF and YAML/TOML
metadata plugins already ship under reaches the release SBOM.

| Candidate | Version | License | Decision |
|---|---|---|---|
| `OfficeIMO.*` | 3.6.2 | MIT | **Adopted.** One maintained, `.NET 10`-targeting reader for seven document families, already used by the HTML metadata plugin, with the bounded load options, the read-only access modes and the protection refusals this plugin needs. Adopting it removes the hand-written OOXML part walker and property parsers. |
| `DocumentFormat.OpenXml` | 3.5.1 | MIT | Used transitively, not referenced directly. It is the object model the library builds on; reaching for it by hand instead of through OfficeIMO would reintroduce the per-format part code this change removes. |
| `NPOI` | 2.8.1 | Apache-2.0 | Rejected. Supports the legacy formats but is a full spreadsheet/document library, and it is not what the rest of the solution's Office work reads through. |
| `OpenMcdf` | 3.4.0 | MPL-2.0 | Not adopted. Maintained and `.NET 10`-targeting, but the legacy summary property sets stay on the small read-only reader described above. MPL-2.0 is Reciprocal/MEDIUM and would pass the gate if it were needed. |
| `Bodu.IO.Compound` | 1.3.0 | MIT | Rejected. Reads CFB and targets `.NET 10`, but the package is new, has one maintainer and under 3,000 total downloads, so it adds supply-chain risk the existing read-only reader avoids. |

No policy file change is made.

## Limitations

- Only the document properties, counts and embedded thumbnail are read. Document text, styles, embedded objects,
  spreadsheet cells, slide text, custom XML parts and user-defined `docProps/custom.xml` properties are out of
  scope; this plugin does not render or extract content.
- `docProps/custom.xml` is not published, because the schema is a fixed cached-data contract.
- A Word document's body is parsed to open it, so a text-heavy `.docx` is the slowest case in the table above.
- Legacy `.doc`, `.xls` and `.ppt` streams are recognized by their standard stream names. A damaged or unusually
  written container may therefore be rejected even if an Office application can repair it.
- Legacy Visio publishes its property sets and `DocumentType=visio`; its drawing streams are not modeled, so no
  page count is available for `.vsd`. The library's binary importer stays unused on purpose: it converts a
  Visio 2003–2010 drawing into an editable model, which is scan work a page count does not justify, and no
  fixture here can produce a real VSD payload to keep that path honest.
- `ApplicationVersion` is the writer's own string (for example `16.0000`), not a parsed version number.
- `SlideCount`, `PageCount`, `WordCount` and `CharacterCount` are whatever the writer recorded; Office does not
  recompute them when a document is saved by another application.
- Protected input is reported, never opened: an encrypted OOXML package publishes identity and the reason but no
  document properties, because the contents cannot be inspected without the key. There is no credential path by
  design.
- The thumbnail preview publishes only what a document already embeds. `wmf`/`emf` OOXML thumbnails and metafile
  clipboard thumbnails in legacy documents are not converted, because no rasterizer is added; such documents keep
  their metadata and show "no preview".
- A legacy bitmap thumbnail is published only with a `BITMAPINFOHEADER` or later header; a header with a palette
  is supported, while the 12-byte bitmap core header is treated as absent.
- OneNote `PageTitles` is capped at 200 titles and 4 KiB, so a very long section or notebook list is truncated
  rather than dropped.

## Testing

`tests/SkyCD.Plugin.Metadata.Office.Tests/` builds fixtures two ways: **structurally valid OOXML packages and
OLE2 compound documents written in memory** (so damage paths and exact expected values stay expressible), and
**real library output** produced by OfficeIMO itself for each of the seven applications, modern and legacy form,
including a password-protected package, a Visio stencil, a OneNote notebook package, both Access profiles and the
Project interchange text. Every value the plugin publishes for a library-written fixture is asserted, so the
suite fails if the library's projections drift.

Coverage includes the descriptor and schema, every published field, omission of absent, blank and zero values,
whitespace folding, date parsing, integer parsing, every damage path, truncation and header checks, the exact
budget charge and read count, cancellation, progress reporting, the host's extension and content-type matching,
failure classification, assembly discovery without a manifest, and offline storage through the real scheduler and
in-memory cache store. The descriptor tests also assert that each extension and MIME type is claimed once, that
both capabilities claim the same sources, and that the neighbouring interchange formats (`.xml`, `.txt`, `.msg`,
`.odt`, `.wpd`, `.pdf`) stay unclaimed here. The family tests assert that the readers never touch a file-system
path, a stream constructor over a path, or a temporary directory. Preview coverage adds an embedded JPEG
published as one artifact, a missing or non-JPEG part, byte and pixel bounds, a legacy clipboard bitmap wrapped as
a bitmap file, a metafile clipboard format, encrypted packages, and a captured thumbnail surviving a reopen
without the plugin.
