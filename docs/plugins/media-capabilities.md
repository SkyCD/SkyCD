# Media extraction and previews

Implement `IMediaMetadataCapability` and/or `IMediaPreviewCapability` in the plugin assembly.
The existing assembly scanner discovers these marker-derived interfaces; normal host-version
compatibility, availability and enabled-state filtering apply before host registration.
No manifest or separate registry is involved. Existing plugin interfaces are unchanged.

Descriptors declare extensions (including the leading dot), MIME content types and a versioned
schema. Either extension or MIME match selects the provider, case-insensitively. Schema IDs
and field keys must remain stable; incompatible schema changes require a new schema version.
Values declare exactly one matching primitive type. Units use stable symbols, for example
`s`, `px`, or `byte`; changing a field's meaning or unit is a schema change.

`MediaExtensionManager.ProcessAsync` returns every matching provider's result. Higher descriptor
priority wins precedence; ties use ordinal plugin ID then capability type name. Consumers that
need a single value select the first successful provider with that field. Results remain
separate: the identity of a value includes plugin ID, plugin version, capability type, schema
ID, schema version and field key. Never flatten results into a dictionary keyed only by field
name, or silently replace previously stored metadata. A failed provider does not suppress others.

The caller supplies a factory for a fresh readable stream per provider. The host owns each
returned stream and disposes it after the worker exits. Plugins receive bounded read access,
not a path, UI control or persistence entity. Reads charge a cumulative byte budget, including
repeated reads. Non-seekable streams support sequential reads. Preview data is copied before
return, with an aggregate artifact byte limit; metadata text is also bounded. Progress and
cancellation use the standard .NET interfaces.

Defaults are 16 MiB source reads per provider, 4 MiB preview/metadata output, two simultaneous
workers per manager, and five seconds per provider (including admission wait). Limits are
configurable through `MediaExecutionLimits`. Exceptions, invalid output and timeouts return
safe failure results and publish sanitized `PluginFailure` telemetry through the existing
failure boundary. Caller cancellation propagates.

Timeouts return promptly even for synchronous or cancellation-ignoring plugin code. Such a
worker retains its concurrency slot and resources until it actually exits; the host observes
late exceptions and disposes its stream then. This is in-process cooperative isolation, not
a security sandbox: terminating arbitrary plugin CPU work or bounding plugin allocations
requires a separate process. Plugins must honor cancellation and avoid retaining source access.

The host stores results durably and keeps them available offline; plugins hold no state of their
own. See `docs/architecture/media-cache-lifecycle.md` for how a descriptor's schema id and version
key the stored result, and why bumping the schema version is what invalidates cached data.

## AbiWord document metadata

`SkyCD.Plugin.AbiWord` implements `IMediaMetadataCapability` and is discovered from assembly
metadata, without `plugin.json`. Enable it through the normal plugin pipeline. During indexing,
its results use the existing media scheduler/cache and remain available offline with the item.
It does not provide thumbnails, render documents, extract body text, or require AbiWord installed.

### Supported input and fields

- `.abw`: native AbiWord XML, with either the AWML namespace or no namespace for older documents.
- `.zabw`: native single-member gzip-compressed AbiWord XML. Compression is detected from bytes,
  so plain XML or gzip under either supported extension works.
- MIME `application/x-abiword` also selects the plugin, independently of the filename.
- `.abw.gz` is not selected by extension alone: the host currently supplies only `.gz`.
  Such a source works when its MIME type is supplied; rename to `.zabw` for filename-only indexing.
  Generic `.gz`, ZIP archives, and other word-processor formats are not claimed.

Schema `skycd.abiword.metadata`, version 1, contains text fields:

| Source | Field |
| --- | --- |
| Recognized container | `Container` (`abw`, also for compressed input) |
| Root `version` attribute | `FormatVersion` |
| `dc.title`, `dc.creator`, `dc.subject`, `dc.description` | `Title`, `Author`, `Subject`, `Description` |
| `dc.publisher`, `dc.contributor`, `dc.rights` | `Publisher`, `Contributor`, `Rights` |
| `dc.type`, `dc.format`, `dc.source`, `dc.language` | `Type`, `Format`, `Source`, `Language` |
| `dc.relation`, `dc.identifier`, `dc.coverage` | `Relation`, `Identifier`, `Coverage` |
| `dc.date`, `abiword.date_last_changed` | `Date`, `LastChanged` |
| `abiword.keywords`, `abiword.generator` | `Keywords`, `Generator` |

Only direct `abiword/metadata/m` entries in the document namespace are read. Unknown/custom
keys and body content are ignored; absent/blank values are omitted. Values are trimmed;
repeated keys keep the last nonblank value. Dates remain text because AbiWord can store
ctime-style local dates without a timezone. `dc.date` is a lifecycle date, not necessarily
creation time; missing/stale dates are not replaced with filesystem timestamps.

### Safety and limitations

The plugin works on .NET 10 on Windows, Linux and macOS without native dependencies. XML
encoding declarations/BOMs are respected. Standard AbiWord DOCTYPE declarations are ignored,
not resolved; external resources are never loaded and custom entities are not expanded.
The entire XML document is parsed before metadata is published. Invalid XML, damaged gzip,
unsupported containers, nested metadata markup, or exceeded limits fail only that provider
through the host's sanitized failure boundary. Cancellation propagates without publishing.

Defaults: 16 MiB source bytes, 32 MiB decoded bytes/XML characters, 64 XML nesting levels,
and 4,096 characters per metadata field. `AbiWordPluginOptions` configures the byte/field
limits; host source/output/time limits still apply. Gzip checksum validation is provided by
`GZipStream`, with an additional size-footer check for truncated trailers. Concatenated gzip
members and trailing data are not supported. No partial metadata is published on failure.

Native ABW password encryption was not established in the inspected upstream format. No
password is requested, guessed, or stored and decryption is not supported. Encrypted or
externally wrapped non-XML input produces an unsupported/read failure, not a false claim
that every damaged file requires a password. AbiWord's `styles="locked"` is a style lock,
not encryption, and does not prevent metadata reading.

### Dependency evaluation (2026-11-20)

The implementation uses .NET 10 `System.Xml.XmlReader` and `System.IO.Compression.GZipStream`
(MIT-licensed .NET runtime), plus the existing plugin abstraction project. `dotnet list
Plugins/SkyCD.Plugin.AbiWord/SkyCD.Plugin.AbiWord.csproj package --include-transitive` reports
no NuGet dependencies. No additional direct/transitive licenses, whitelist entries or policy
changes are needed; the blocked-license, unknown-license and explicit-whitelist rules in
`docs/legal/dependency-license-policy.json` remain unchanged.

Alternatives considered before adoption:

- **SharpZipLib 1.4.2**: MIT, dependency-free net6.0 assets compatible with .NET 10. It supplies
  gzip but no AbiWord semantics; its latest release is from 2023 and recent maintenance was
  not established. It adds no useful capability over the runtime here.
- **Aspose.Words 26.11.0**: maintained releases and explicit net10.0 assets, but the published
  format list does not establish ABW support. Its proprietary EULA is not policy-approved.
- **GroupDocs.Parser 26.10.0**: current release with a .NET 10 package variant, but its format
  matrix does not establish ABW metadata support. Its proprietary EULA is not policy-approved.
- Neither EULA package is whitelisted; their transitive graphs were not certified after
  direct-license/functional rejection. AbiWord itself is GPL-2.0-or-later and is not linked,
  bundled, or copied into this implementation; upstream files are format references only.

References: [AbiWord formats](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/abiword.1#L72-L73),
[metadata keys](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/src/text/ptbl/xp/pd_Document.h#L95-L165),
[metadata writer](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/src/wp/impexp/xp/ie_exp_AbiWord_1.cpp#L1173-L1213),
[upstream document](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/test/wp/table.abw#L1-L19),
[.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT),
[SharpZipLib](https://www.nuget.org/packages/SharpZipLib),
[Aspose.Words](https://www.nuget.org/packages/Aspose.Words/),
[GroupDocs formats](https://docs.groupdocs.com/parser/net/supported-document-formats/).

## Apple iWork document metadata

`SkyCD.Plugin.IWork` implements `IMediaMetadataCapability` and is discovered from assembly
metadata, without `plugin.json`. Enable it through the normal plugin pipeline. During indexing,
its results use the existing media scheduler/cache and remain available offline with the item.
It does not provide thumbnails, decode document content, or require iWork installed.

### Supported input and fields

- `.pages`, `.numbers`, `.key`: modern iWork packages, a ZIP container with either the component
  archive stored directly as `Index/*.iwa` or bundled as a nested `Index.zip`.
- MIME `application/x-iwork-pages-sffpages`, `application/x-iwork-numbers-sffnumbers` and
  `application/x-iwork-keynote-sffkey` also select the plugin, independently of the filename.
- Legacy iWork '09 packages (`index.xml` / `index.apxl`) are recognized and reported as unsupported
  rather than partially catalogued. Other formats are not claimed.

Schema `skycd.iwork.metadata`, version 1:

| Source | Field |
| --- | --- |
| Recognized package | `Container` (`iwork`) |
| Extension, MIME type, or component inventory | `DocumentType` (`pages`, `numbers`, `keynote`) |
| Root `.iwpv2` verifier | `IsPasswordProtected` |
| `Metadata/Properties.plist` `fileFormatVersion` | `FormatVersion` |
| Latest non-template `Metadata/BuildVersionHistory.plist` entry | `ProducerBuild` |
| `Metadata/DocumentIdentifier`, then `documentUUID` | `DocumentIdentifier` (canonical UUID) |
| `Properties.plist` `language`, `locale` | `Language`, `Locale` |
| `Properties.plist` `isMultiPage`, external-data flag | `IsMultiPage`, `HasExternalData` |
| Root-level preview image | `HasPreview` |
| Preview PNG/JPEG header | `PreviewWidth`, `PreviewHeight` (`px`) |
| `Index` component and `Data/` entry counts | `IwaComponentCount`, `DataAssetCount` |
| Keynote `Index/Slide-*` components | `SlideCount` |

Absent or blank values are omitted, so "unknown" stays distinguishable from "none". Counts describe
the package inventory, not rendered content: a component may belong to a template or another
object, and `SlideCount` is the one count with a precise meaning. When a mislabelled package has no
inventory marker either, `DocumentType` is omitted rather than guessed.

### Password protection, safety and limitations

A protected modern package is still a readable ZIP: protection encrypts member payloads, not the
container. `Metadata/Properties.plist` and `Metadata/DocumentIdentifier` stay plain, so the document
is catalogued with `IsPasswordProtected = true` and its plain metadata. Encrypted members (build
history, preview, data) are not decoded and their fields are omitted. No password is requested,
guessed, or stored, and there is no credential path by design.

The plugin works on .NET 10 on Windows, Linux and macOS without native dependencies. Sources are
read once through the bounded `IMediaSource`; the plugin never receives a path and no IWA component
is decoded. Property lists are parsed as XML with DOCTYPE processing disabled and no resolver, so
the Apple DTD and any external entity are never fetched. Defaults: 64 MiB source, 1 MiB per
metadata member, 32 MiB nested index, 64 KiB preview header; the host's own limits still apply.
Damaged, truncated, empty or index-less ZIPs, legacy packages, oversized sources and short reads
fail only that provider through the host's sanitized failure boundary. Cancellation propagates
without publishing.

Limitations: no title, author, subject or creation/modification dates, because the plain members
do not carry them and compressed IWA components are not decoded. There is no OCR or text excerpt,
one preview candidate is used per package, and preview dimensions describe Apple's stored thumbnail
rather than the page size.

### Dependency evaluation (2026-11-20)

The implementation uses .NET 10 `System.IO.Compression.ZipArchive` and `System.Xml.Linq`
(MIT-licensed .NET runtime), plus the existing plugin abstraction project. `dotnet list
Plugins/SkyCD.Plugin.IWork/SkyCD.Plugin.IWork.csproj package --include-transitive` reports no
NuGet dependencies. No additional direct/transitive licenses, whitelist entries or policy changes
are needed; the blocked-license, unknown-license and explicit-whitelist rules in
`docs/legal/dependency-license-policy.json` remain unchanged.

No maintained .NET library exposing iWork package metadata was identified during evaluation. The
members this plugin publishes are plain ZIP entries, so the runtime covers the need without adding
a dependency, and no native or proprietary component is required or bundled.

References: [iWork package format notes](https://github.com/obriensp/iWorkFileFormat),
[OpenDocument.core iWork reader](https://github.com/opendocument-app/OpenDocument.core),
[.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
