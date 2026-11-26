# Media extraction and previews

Implement `IMediaMetadataCapability` and/or `IMediaPreviewCapability` in the plugin assembly.
The existing assembly scanner discovers these marker-derived interfaces; normal host-version
compatibility, availability and enabled-state filtering apply before host registration.
No manifest or separate registry is involved. Existing plugin interfaces are unchanged.

Descriptors declare extensions (including the leading dot), MIME content types and a versioned
schema. Either extension or MIME match selects the provider, case-insensitively. Claiming an
extension is not ownership: several plugins may support the same one, and the optional format
probe described below is how a plugin rejects content that only shares it. Schema IDs
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
returned stream and disposes it after the worker exits. Byte-reading plugins receive bounded read
access, not a path, UI control or persistence entity; the path-addressed contracts are
`ILocalMediaMetadataCapability` and `ILocalMediaPreviewCapability`, described below. Reads charge a
cumulative byte budget, including
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

## Shared extensions and format probes

Several plugins can claim the same extension, and neither extension nor MIME ownership decides
which one is right for a file. `.prefab` is Unity YAML in one project and O3DE, Flax or Cocos
JSON in another; `.scene` is Cocos, Flax or s&box; `.material` is Unity YAML or O3DE/Flax JSON;
`.mdl` is a Digitrakker module or a Quake model; `.mat` is Unity YAML or MATLAB; `.tsx` is
TypeScript or a Tiled tileset. Without a probe every matching provider runs, and a foreign file
either captures an empty record or fails, marking the entry failed even when another provider
read it fine.

A metadata or preview capability that also implements `IMediaFormatProbe` is asked, before
extraction, whether the bounded header really is its format:

```csharp
public interface IMediaFormatProbe
{
    ValueTask<bool> CanHandleAsync(MediaItem item, IMediaSource source, CancellationToken cancellationToken);
}
```

`true` runs extraction as usual. `false` means "definitely not this format": the host skips only
that provider for the item — no result, no cache record, no `PluginFailure` and no entry-level
failure — while the other providers and the generic file metadata continue. Claiming an
extension is therefore a cheap filter, not a promise; a plugin that cannot probe keeps today's
behaviour. Probes are deliberately conservative: return `false` only when the header is
conclusive, and return `true` when unsure, because a wrong `false` silently loses metadata.
Providers that need more than a magic prefix (a `$type`/`TypeName` marker inside Unity YAML, a
ZIP entry lookup for `.c3p`/`.xrns`, gzip-then-root-element detection for `.als`/`.mmpz`, or
`project.xml` plus an XML root for `.drp`) can express it here without the host growing a
format-description language. When one plugin has both a metadata and a preview class, a shared
parse helper should own the probe logic.

The host bounds the probe itself instead of trusting the plugin: it runs under `ProbeBytes`
(default 64 KiB) and `ProbeTimeoutMilliseconds` (default 500 ms) on `MediaExecutionLimits`, with
a separate budget from extraction. After an accepted probe, extraction reads from offset 0 with
the full source budget; the bytes the probe already consumed are replayed from memory, so a
non-seekable source works too. A probe that throws or exceeds its budget is a rejection, not a
failure — a foreign file is expected input, so nothing is published. Only a provider that
probes `true` and then throws during extraction produces the usual failed record.

Rejections are per extraction and are not persisted: the provider simply has no record for that
file, `ResolveProviders` and cache identity never invoke a probe (they stay IO-free), and a
rejected file with a record from an older plugin version is left to the normal supersede/stale
logic. Multiple providers may accept and all still capture; priority only orders their results
and the first thumbnail, and there is no exclusivity or conflict resolution.

Probes must be read-only and side-effect free: no writes, no referenced or sibling files, no
state that outlives the call, and no persisted paths. Path-addressed capabilities implement
`ILocalMediaFormatProbe` with the same rules against `LocalMediaContext`; it is only asked while
a validated local source path exists, so the existing skip rule still applies without one, and
preview-only capabilities may probe too.

## OS-assigned metadata

Some metadata is not in the file at all: Explorer tags, comments and ratings, or the extended
attributes a file manager writes beside the bytes. Such values cannot be read through
`IMediaSource`, so `ILocalMediaMetadataCapability` is the one path-addressed contract. The host
passes a `LocalMediaContext(Path, Extension, ContentType, Length)` instead of a stream, and only
while the source is available locally and the path has been validated.

The path is host-owned: a provider must not persist, log or publish it, and it must never appear in
a value or artifact name. When no path is available — an offline entry, or an index pass over
cached facts — the provider is **skipped**: it contributes no result at all, not an empty one and
not a failure. A failure would mark values already captured for that entry as stale, so a platform
that simply has nothing to say must stay silent. Extraction is read-only; a provider never writes
attributes back.

Descriptors, schema and value validation, sanitized failure reporting, cancellation and the output
limits are identical to `IMediaMetadataCapability`. Selection treats these providers as metadata
providers for `MediaProviderSelection.StandardMetadata` and `OptInMetadata`; whole-file hashing and
preview passes do not select them. Adding the interface and its context record is a MINOR SDK
addition under `docs/plugins/sdk-versioning-policy.md`; existing capabilities are unchanged.

## OS-addressed previews

Some artwork is not in the source bytes either: a freedesktop launcher's `Icon=` file, a desktop
theme image stored on the scanned volume, or a bundle icon in a sibling folder. Such an icon cannot
be reached through `IMediaSource`, so `ILocalMediaPreviewCapability` receives the same
`LocalMediaContext(Path, Extension, ContentType, Length)` instead of a stream, under exactly the
same host-owned path rules as `ILocalMediaMetadataCapability`.

It is the preview counterpart of the metadata contract:

- The descriptor declares extensions, content types, schema and priority like any preview, and may
  be marked `IndexTimePreview` so the indexing pass stores the artwork with the metadata.
- The provider is selected by preview passes only — `Standard` and `StandardMetadata` (where an
  index-time preview runs); a metadata-only opt-in pass does not select it, because a source whose
  bytes carry no metadata has nothing to say to that pass.
- When no local path is available it is **skipped**: it contributes no result at all, not an empty
  one and not a failure, so an offline entry keeps the preview the catalog already stored.
- The path is never persisted, logged or published, and never appears in an artifact name. Because
  the provider reads the real file rather than a bounded stream, it applies its own file-size bound
  and publishes only bounded artifacts through the same copied-artifact output limits.

## AbiWord document metadata and thumbnail

`SkyCD.Plugin.Metadata.AbiWord` implements `IMediaMetadataCapability` and `IMediaPreviewCapability` and is
discovered from assembly metadata, without `plugin.json`. Enable it through the normal plugin pipeline. During
indexing, its results use the existing media scheduler/cache and remain available offline with the item. The
thumbnail pass recovers the leading body text and draws it to a page image; AbiWord itself is not required.

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

### Rendered page thumbnail

`AbiWordPreviewPlugin` publishes schema `skycd.abiword.preview`, version 1, which is identity only because the
result is the image, not values. The thumbnail walks the same AWML tree the metadata pass accepts and collects
the leading body text:

- Only direct `abiword/section/p` paragraphs are read. Runs inside a paragraph are concatenated, and paragraphs
  are joined by line breaks. Self-closing and whitespace-only paragraphs contribute no line.
- A block nested inside a paragraph, such as a table, contributes nothing; tables, lists, endnotes, headers and
  footers live in sibling elements and are not walked.
- The same XML hardening applies: the DOCTYPE is ignored, custom entities are never expanded, undeclared named
  entities are parse errors, and no external resource is resolved. Character and predefined entity references
  are decoded.
- Collection stops at `MaxRenderedCharacters` (20,000) or `MaxRenderedParagraphs` (200) leading paragraphs, so a
  large body costs no more than the sample it produces. `maxThumbnailDimension` is 768.

`SkiaAbiWordThumbnailRenderer` draws the trimmed text as a white page with black sans-serif text: the page width
equals `MaxThumbnailDimension`, the height follows the A-series page aspect, and lines wrap inside a proportional
margin. The result is a single `image/png` artifact and progress reports 0.5 before drawing and 1 on completion.
A renderer that cannot run reports no preview before the source is read, and text with nothing to draw produces
an empty artifact list, which is the host's explicit "no preview" state.

A document whose XML cannot be parsed as AbiWord reports no preview without failing the pass, so its metadata
is still published. A damaged gzip container, an oversized source or a mixed-namespace body keeps the reader's
own classification (`AbiWordDecodeException` / `AbiWordLimitExceededException`), and cancellation propagates.

### Safety and limitations

The metadata pass works on .NET 10 on Windows, Linux and macOS without native dependencies;
the thumbnail pass adds the SkiaSharp native rasterizer and degrades to "no preview" when it
cannot load. XML encoding declarations/BOMs are respected. Standard AbiWord DOCTYPE declarations are ignored,
not resolved; external resources are never loaded and custom entities are not expanded.
The entire XML document is parsed before metadata is published. Invalid XML, damaged gzip,
unsupported containers, nested metadata markup, or exceeded limits fail only that provider
through the host's sanitized failure boundary; unparseable XML only suppresses the thumbnail.
Cancellation propagates without publishing.

Defaults: 16 MiB source bytes, 32 MiB decoded bytes/XML characters, 64 XML nesting levels,
4,096 characters per metadata field, a 768-pixel longest thumbnail edge, and 20,000 leading body
characters across at most 200 paragraphs for the thumbnail. `AbiWordPluginOptions` configures these
limits; host source/output/time limits still apply. Gzip checksum validation is provided by
`GZipStream`, with an additional size-footer check for truncated trailers. Concatenated gzip
members and trailing data are not supported. No partial metadata is published on failure.

Native ABW password encryption was not established in the inspected upstream format. No
password is requested, guessed, or stored and decryption is not supported. Encrypted or
externally wrapped non-XML input produces an unsupported/read failure, not a false claim
that every damaged file requires a password. AbiWord's `styles="locked"` is a style lock,
not encryption, and does not prevent metadata reading.

### Dependency evaluation (2026-11-20)

The metadata implementation uses .NET 10 `System.Xml.XmlReader` and `System.IO.Compression.GZipStream`
(MIT-licensed .NET runtime), plus the existing plugin abstraction project. The thumbnail pass adds
`SkiaSharp` and `SkiaSharp.NativeAssets.{Linux,macOS,Win32}` 3.119.4 (MIT), the same package set the RTF and
WordPerfect preview plugins use; the metadata path itself still pulls no NuGet dependency. No additional
direct/transitive licenses or policy changes are needed; the Trivy classification gate in
`docs/legal/dependency-policy.md` remains unchanged.

Alternatives considered before adoption:

- **SharpZipLib 1.4.2**: MIT, dependency-free net6.0 assets compatible with .NET 10. It supplies
  gzip but no AbiWord semantics; its latest release is from 2023 and recent maintenance was
  not established. It adds no useful capability over the runtime here.
- **Aspose.Words 26.11.0**: maintained releases and explicit net10.0 assets, but the published
  format list does not establish ABW support. Its proprietary EULA is unacceptable.
- **GroupDocs.Parser 26.10.0**: current release with a .NET 10 package variant, but its format
  matrix does not establish ABW metadata support. Its proprietary EULA is unacceptable.
- Neither EULA package was adopted; their transitive graphs were not certified after the
  functional and license review. AbiWord itself is GPL-2.0-or-later and is not linked,
  bundled, or copied into this implementation; upstream files are format references only.

References: [AbiWord formats](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/abiword.1#L72-L73),
[metadata keys](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/src/text/ptbl/xp/pd_Document.h#L95-L165),
[metadata writer](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/src/wp/impexp/xp/ie_exp_AbiWord_1.cpp#L1173-L1213),
[upstream document](https://github.com/AbiWord/abiword/blob/6940426ea7fd3810f1aa5477fa93c2d4278dea7d/test/wp/table.abw#L1-L19),
[.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT),
[SharpZipLib](https://www.nuget.org/packages/SharpZipLib),
[Aspose.Words](https://www.nuget.org/packages/Aspose.Words/),
[GroupDocs formats](https://docs.groupdocs.com/parser/net/supported-document-formats/).

## Apple iWork document metadata and thumbnail

`SkyCD.Plugin.Metadata.IWork` implements `IMediaMetadataCapability` and `IMediaPreviewCapability` and is
discovered from assembly metadata, without `plugin.json`. Enable it through the normal plugin
pipeline. During indexing, its results use the existing media scheduler/cache and remain available
offline with the item. It does not render pages, decode document content into text, or require
iWork installed.

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
| IWA component archive root; otherwise extension, MIME type, or component inventory | `DocumentType` (`pages`, `numbers`, `keynote`) |
| Root `.iwpv2` verifier | `IsPasswordProtected` |
| `Metadata/Properties.plist` `fileFormatVersion` | `FormatVersion` |
| Latest non-template `Metadata/BuildVersionHistory.plist` entry | `ProducerBuild` |
| `Metadata/DocumentIdentifier`, then `documentUUID` | `DocumentIdentifier` (canonical UUID) |
| `Properties.plist` `language`, `locale` | `Language`, `Locale` |
| `Properties.plist` `isMultiPage`, external-data flag | `IsMultiPage`, `HasExternalData` |
| Recognized embedded thumbnail | `HasPreview` |
| Preferred embedded raster preview | `PreviewWidth`, `PreviewHeight` (`px`) |
| `Index` component and `Data/` entry counts | `IwaComponentCount`, `DataAssetCount` |
| Keynote slide projection | `SlideCount` |

Absent or blank values are omitted, so "unknown" stays distinguishable from "none". A parseable
package takes its type from the component archive: a Pages/Numbers/Keynote document is identified
even when the file is mislabelled. Only a protected package, which is not parsed, falls back to the
filename, MIME type and component inventory.

### Thumbnail preview

Schema `skycd.iwork.preview`, version 1, carries identity only. The capability publishes the
document's own embedded raster thumbnail (Apple's `preview.jpg` / `preview.png`, including the
`preview-web`, `preview-micro` and Quick Look variants the reader recognizes) as one artifact with
its declared MIME type, bounded by 4 MiB. Nothing is rendered. A package without a valid raster
preview, or a protected package, returns an empty artifact list, which the host records as an
explicit "no preview".

### Password protection, safety and limitations

A protected modern package is still a readable ZIP: protection encrypts member payloads, not the
container. `Metadata/Properties.plist` and `Metadata/DocumentIdentifier` stay plain, so the document
is catalogued with `IsPasswordProtected = true` and its plain metadata. The component archive is not
parsed for a protected package, so encrypted members (build history, preview, data) are never
decoded and their fields are omitted; no thumbnail is published. No password is requested, guessed,
or stored, and there is no credential path by design.

The plugin works on .NET 10 on Windows, Linux and macOS without native dependencies. Sources are
read once through the bounded `IMediaSource`; the plugin never receives a path, and OfficeIMO parses
only the bytes it was given, so an external reference inside a package is never followed. Property
lists are parsed as XML with DOCTYPE processing disabled and no resolver, so the Apple DTD and any
external entity are never fetched. Defaults: 64 MiB source (also the parser's package bound), 1 MiB
per metadata member, 32 MiB nested index, 4 MiB thumbnail; the host's own limits still apply.
Damaged, truncated, empty or index-less ZIPs, legacy packages, oversized sources and short reads
fail only that provider through the host's sanitized failure boundary. Cancellation propagates
without publishing.

Limitations: no title, author, subject or creation/modification dates, because the plain members do
not carry them and the reader does not expose them; there is no OCR or text excerpt, and the
thumbnail is Apple's stored image rather than a rendered page.

### Dependency evaluation (2026-11-20)

The implementation uses **OfficeIMO.IWork 3.6.2** (MIT, explicit `net10.0` assets, active
maintenance) for the component archive: it identifies the application from the IWA root, reads the
build history, projects Keynote slides and recognizes and validates embedded preview images. Its
only dependency is `OfficeIMO.Core` 3.6.2 (also MIT, no further dependencies). Both pass the CI
license gate: the committed `packages.lock.json` files scanned by Trivy with the rules in
[dependency-policy.md](../legal/dependency-policy.md) report full compliance, and the classification
gate is unchanged.

Alternatives considered:

- **Custom ZIP and IWA parsing** (the previous iteration): no dependency, but document type had to
  be guessed from component names, slide counts from `Index/Slide-*` filenames, and the preview
  dimensions from a hand-written PNG/JPEG header sniffer. It could not identify a mislabelled
  document from its IWA root and treated synthetic fixtures as evidence. Rejected in favour of the
  maintained parser, which exercises real packages.
- The library also ships creation, editing and preservation-aware saving. That surface is unused;
  only the read path is called, with whole-content projections run on bounded sources.

References: [iWork package format notes](https://github.com/obriensp/iWorkFileFormat),
[OpenDocument.core iWork reader](https://github.com/opendocument-app/OpenDocument.core),
[OfficeIMO.IWork](https://www.nuget.org/packages/OfficeIMO.IWork),
[.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).

## Video project metadata (Premiere Pro, After Effects, Resolve, VEGAS Pro)

`SkyCD.Plugin.Metadata.VideoProjects` implements `IMediaMetadataCapability` at priority 10 and is
discovered from assembly metadata, without `plugin.json`. It reports the shape of an editing project —
application and version, timeline names and counts, tracks, clips, items, referenced-media count, frame
rate, duration and resolution — and never opens the referenced media. There is no preview capability and
no artwork pass-through.

### Supported input and fields

The container is identified from its bytes, not its extension, so a renamed or unrelated file publishes
nothing.

| Source | Container | Facts read |
| --- | --- | --- |
| Premiere Pro `.prproj` | XML, plain or gzip (sniffed), root `<PremiereData>` | `Project` version, `Sequence` names, video/audio `Track` counts, first `FrameRate`/`FrameRect`, clip `Start`/`End` ticks, project-item counts, distinct file-system `Media` paths |
| After Effects `.aep` | big-endian RIFX `Egg!` chunk tree | `head`/`svap` version word, first `Fold` item tree (`idta` type), first composition `cdta` size, rate and duration |
| After Effects `.aepx` | XML root `<AfterEffectsProject>` | the same facts from the hexadecimal `bdata` chunk bodies and the item `string` names |
| DaVinci Resolve `.drp` / `.drt` | ZIP: `project.xml`, `MediaPool/Master/MpFolder.xml`, `SeqContainer/*.xml` | `DbAppVer`, timeline clip names, track vectors, clip start/duration frames and `MediaFrameRate`; `.drt` is flagged as a template |
| VEGAS Pro `.veg` | Sonic Foundry RIFF header: `riff` magic, VEGAS project and project-settings GUIDs | the stored application version (offset 70, little-endian 16-bit) |

Schema `skycd.videoprojects.metadata`, version 1: `Kind` (Premiere, AfterEffects, Resolve, Vegas), `Format`
(prproj, aep, aepx, drp, drt, veg), `Application`, `ApplicationVersion`, `ProjectTitle`, `TimelineCount`,
`TimelineNames`, `TrackCount`, `ClipCount`, `ItemCount`, `MediaFileCount`, `FrameRate` (`fps`),
`Duration` (`s`), `Resolution`, `IsTemplate`, `Truncated`. Absent or blank values are omitted, and
`IsTemplate`/`Truncated` are published only when true.

### Safety and limitations

- Premiere's tick clock is 254 016 000 000 ticks per second; clip ends become `Duration`. Built-in
  generators such as Black Video store an internal id instead of a path, so only path-shaped `Media`
  values count as referenced files, deduplicated case-insensitively.
- Gzip output is read through a counting stream with a 64 MiB decompressed bound, so a small file
  cannot expand without limit. The ZIP readers never extract: entries are located by expected name,
  and each entry and the total inflated bytes are bounded before inflation, so a declared-size bomb
  is refused rather than read.
- The RIFX walk validates every chunk size against its parent, visits at most 100,000 chunks and reads
  only the prefix of a body it needs. An overrunning or over-declared size stops the walk, keeps the
  facts recovered so far and publishes `Truncated = true`; the file is not failed.
- XML is parsed with `DtdProcessing.Prohibit`, a null resolver, a document character cap and a depth
  limit, so a DTD or external entity is never resolved. A DTD-bearing or malformed "project" publishes
  no values instead of failing the scan, like an unrecognized container.
- Fixed plugin defaults: 64 MiB project XML, 32 MiB archive entry, 64 MiB total inflated archive,
  10,000 archive entries, 1,024 timeline documents, 100,000 chunks, 32 published timeline names and
  256-character names. The host's source and output budgets still apply; a source that exceeds them
  fails only this provider.
- VEGAS Pro `.veg` is read only after a real project confirmed the container: the 72-byte header must carry
  the `riff`, VEGAS project and project-settings GUIDs with a form size equal to the file length, and a
  settings chunk that fits inside it; anything else publishes no values. Only the stored version is mapped —
  timelines, tracks and events stay unread until their layout is confirmed (#1285). Movie Studio `.vf` is not
  claimed: no genuine sample proved its application signature yet (#1285).

### Dependency evaluation (2026-11-26)

The implementation uses only the .NET 10 base class library — `System.Xml.XmlReader`,
`System.IO.Compression.GZipStream` and `System.IO.Compression.ZipArchive` (MIT-licensed .NET runtime) —
plus the plugin abstraction project. No NuGet package provides these four private project formats, so
adding one would not replace the parsers, and no new direct or transitive license is introduced. The
Trivy classification gate in [dependency-policy.md](../legal/dependency-policy.md) is unchanged.

References: [Premiere project XML](https://github.com/Adobe-CEP/CEP-Resources),
[After Effects project structure](https://github.com/boltframe/aftereffects-aep-parser),
[.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).

## Game engine project and asset metadata (Godot, Unity, Unreal, Aseprite, Tiled, LDtk)

`SkyCD.Plugin.Metadata.GameDev` implements `IMediaMetadataCapability` at priority 10 plus five index-time
pixel previews and one on-demand structure preview, all discovered from assembly metadata. It publishes
family, format, versions, sizes, counts and bounded name samples for 22 engine families and never opens a
referenced tileset, script, GUID target or media file. Extensions that several engines or the text plugin
share (`.prefab`, `.scene`, `.material`, `.mat`, `.mtl`, `.scn`, `.meta`, `.anim`, `.tsx`, `.go`,
`.project`) are claimed behind the shared format probe (#1258): the host reads a 64 KiB header and rejects
TypeScript, Go source, Eclipse projects, Wavefront materials and other foreign content before extraction.

Preview providers are marked `IndexTimePreview` only for artwork the catalog list shows: Aseprite frame 0,
Unity package `preview.png`, the Unreal package thumbnail, the Wicked Engine scene JPEG and uncompressed XNB
textures. Structure pages run on demand. See [gamedev.md](gamedev.md) for the coverage table, schema,
bounds and dependency evaluation (AsepriteDotNet 1.9.1 and SkiaSharp 3.119.4, both MIT).
