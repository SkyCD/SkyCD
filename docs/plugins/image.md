# Image preview and metadata plugin

Implements #438 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Image/` → plugin id `SkyCD.Plugin.Image`.
Tests: `tests/SkyCD.Plugin.Metadata.Image.Tests/`.

## What it provides

The original capabilities retain their schema identities:

| Capability | Type | Schema | Claims |
|---|---|---|---|
| Typed image metadata and embedded tags | `ImageMetadataPlugin : IMediaMetadataCapability` | `skycd.image.metadata` v2 | `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`; `image/jpeg`, `image/png`, `image/webp`, `image/gif` |
| Bounded thumbnail | `ImagePreviewPlugin : IMediaPreviewCapability` | `skycd.image.preview` v1 | same as metadata |
| Tags from TIFF containers | `ContainerTagsPlugin : IMediaMetadataCapability` | `skycd.image.tags` v1 | `.tif`, `.tiff`; `image/tiff` |

Selection is by descriptor, so an unsupported type is never claimed and the host records its own
`unsupported` marker instead. The tags-only capability reads TIFF metadata without decoding pixels.

Discovery uses assembly metadata and the implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Width` | Integer | `px` | Display width **after** EXIF rotation |
| `Height` | Integer | `px` | Display height **after** EXIF rotation |
| `Format` | Text | – | Container in lowercase: `jpeg`, `png`, `webp`, `gif` |
| `HasAlpha` | Boolean | – | Container can carry transparency |
| `Orientation` | Integer | – | EXIF orientation as stored, 1–8; 1 when absent |
| `RotationDegrees` | Integer | `deg` | Clockwise rotation needed to display upright |
| `FrameCount` | Integer | – | Frames in the container; 1 for a still image |
| `IsAnimated` | Boolean | – | `FrameCount > 1` |
| `CapturedAt` | Timestamp | – | EXIF capture date; **omitted entirely** when the source has none |
| `Tags` | Text | – | Embedded keywords joined with `; `; omitted when the source carries none |
| `Comment` | Text | – | Embedded caption or comment; omitted when the source carries none |
| `Rating` | Integer | – | Embedded star rating 1–5; omitted when absent or rejected |

`Width`/`Height` are the oriented dimensions, so a 4000×3000 photo stored with orientation 6 reports
3000×4000 — the size a viewer shows. `Orientation` and `RotationDegrees` are published alongside so a host
can tell whether the numbers were already corrected.

Version 2 added `Tags`, `Comment` and `Rating`. `Tags` carries the `Tags` role, so the host assigns
the keywords as catalog tags through the existing media-cache effects. There is no hierarchical
expansion: `lr:hierarchicalSubject` is not read, so `Places|Beach|Sunset` stays one keyword. Bare XMP
sidecars are claimed by a separate plugin (`docs/plugins/xmp.md`), and OS-assigned properties by
`SkyCD.Plugin.Metadata.FileMetadata` (`docs/plugins/file-metadata.md`); each carrier is claimed once.

Provenance is not a field: it is the record identity the host stores (plugin id, plugin version, capability
type, schema id, schema version), per `docs/architecture/media-cache-lifecycle.md`. A capture date is never
invented — an absent EXIF date produces no `CapturedAt` value rather than a fallback to the file timestamp.

## Embedded tag carriers

Tags are read from the same buffered bytes the metadata pass already holds, chosen by magic bytes
rather than extension:

| Container | Carriers |
|---|---|
| JPEG | APP1 Exif (TIFF: `XPKeywords`, `XPComment`, `ImageDescription`, `UserComment`, `Rating`, `RatingPercent`), APP1 XMP, APP13 Photoshop/IPTC-IIM, COM comment |
| PNG | `tEXt` and uncompressed `iTXt` (`Keywords`, `Comment`, `Description`), uncompressed XMP `iTXt`, `eXIf` |
| WebP | `XMP ` packet, `EXIF` chunk |
| GIF | none — no portable tag carrier |

Compressed PNG text (`zTXt`, compressed `iTXt`) is inflated by the library before mapping, so a tool that
compresses its caption still reaches the catalog. XMP provides `dc:subject`, `pdf:Keywords`,
`dc:description`, `exif:UserComment`, `xmp:Rating` and `MicrosoftPhoto:Rating`; IPTC-IIM provides
2:25 keywords and 2:120 caption; TIFF ratings use the Shell 1–99 scale, negative values mean
"rejected" and are ignored. Readers run from the most explicit carrier to the least — XMP, IPTC-IIM,
the EXIF directory, PNG text chunks, JPEG COM — so a caption or rating written by a photo editor wins,
while keywords from every carrier merge. The directory parsing comes from `MetadataExtractor`; only the
portable-property mapping and the XMP property mapping are shared source files from
`Plugins/Shared/MediaMetadata/`, compiled into this assembly and into the XMP sidecar plugin.

## Tags from TIFF

`ContainerTagsPlugin` covers the containers the decoder is not asked to present. It selects by magic bytes,
not extension: a file named `.tif` that does not start with a TIFF header publishes no values instead of
failing the pass.

| Key | Type | Meaning |
|---|---|---|
| `Tags` | Text | Keywords joined with `; `; omitted when none |
| `Comment` | Text | Caption or comment; omitted when none |
| `Rating` | Integer | Star rating 1–5 from the Shell 1–99 or 1–5 scales; omitted when absent or rejected |

Carriers read from the TIFF directory structure: `XPKeywords`/`XPComment` (UCS-2LE), `ImageDescription`,
`UserComment`, `Rating` and `RatingPercent`, plus the embedded XMP (tag 700) and IPTC-IIM (tag 33723)
blobs, which are mapped by the same reader as the JPEG path. Maker notes are not mapped: they are
per-vendor and not portable. The scan reads at most the first 1 MiB of the source, in 256 KiB chunks and
through the host's budget. Damaged directories contribute nothing
rather than failing extraction.

## Preview artifacts

One artifact per image:

- `image/png` when the source has an alpha channel, otherwise `image/jpeg` at quality 85.
- Longest edge capped at 512 px, aspect ratio preserved, never upscaled, and never shorter than 1 px on the
  short edge.
- EXIF orientation is applied while rendering, so the stored thumbnail is already upright.
- Well under the host's 4 MiB artifact budget for any supported input.

The original is only ever read. Nothing is written back, and a test asserts the source bytes are unchanged
after both extraction and preview generation.

### Animation policy

**Animated containers are previewed by their first frame only.** `FrameCount` and `IsAnimated` in the
metadata schema let a host label the thumbnail as a still taken from an animation. No frame other than the
first is decoded or stored, so animation length cannot inflate the cache.

## Resource behaviour

The plugin reads through the host's bounded `IMediaSource` and never sees a path.

`ImageSourceReader` reads the whole source into a buffer in 256 KiB chunks **before** any native decoder sees
it. That is deliberate rather than convenient: Skia pulls bytes from a managed stream through a native
callback, and an exception raised inside that callback — including the host's own read-budget exception —
cannot cross back into managed code. It terminates the process instead of failing the provider. Reading
first keeps every failure on the plugin's own stack, where it becomes an ordinary provider error. The
consequence is that a legacy raster source must fit inside the host's source budget to be processed, which is the
budget's purpose; a regression test asserts that an exhausted budget surfaces as an exception rather than a
crash.

- Metadata extraction decodes no pixels: geometry comes from the container header, tags from header
  directories and metadata segments, and JPEG reading stops at Start-Of-Scan.
- A source above `ImagePluginOptions.MaxSourceBytes` (64 MiB, deliberately above the host's 16 MiB default)
  is rejected before a single byte is charged, so the host budget is what normally binds.
- Cancellation is checked before the first read, after decode and before encode. Decode itself is not
  interruptible; the host's per-provider timeout covers a decoder that ignores cancellation.
- Malformed and truncated sources raise `ImageDecodeException`, which the host turns into a `failed` record
  with a sanitized reason. Failures never surface plugin exception text to the catalog.
- A malformed EXIF directory is not a failure. The library reports the directories it could read, and a
  container it cannot parse at all yields no tags while the geometry already extracted from the decoder
  stands.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `MetadataExtractor` | 2.9.3 | Apache-2.0 | EXIF, IPTC-IIM, XMP, PNG, WebP and TIFF directories |
| `XmpCore` (transitive) | 6.1.10.1 | BSD-3-Clause | XMP packet parsing; declared by legacy URL, reported as UNKNOWN |
| `SkiaSharp` | 3.119.4 | MIT | Decode, orientation-aware scaling, thumbnail encoding |
| `SkiaSharp.NativeAssets.Win32` / `.Linux` / `.macOS` | 3.119.4 | MIT | Platform natives |

`MetadataExtractor` 2.9.3 targets .NET 8 and therefore runs on .NET 10 without a compatibility package.
Its `XmpCore` dependency declares only a legacy license URL instead of an SPDX expression, so the Trivy
scan in [dependency-policy.md](../legal/dependency-policy.md) reports it as UNKNOWN and does not fail;
Adobe's XMP SDK `LICENSE` and the upstream project README are the BSD-3-Clause evidence. All packages
pass the gate from the committed `packages.lock.json` files.

## Raster limitations

- JPEG, PNG, WebP and GIF are decoded. TIFF, HEIF, AVIF and RAW are never decoded; TIFF-based containers
  are claimed by the tags-only capability, and OS-assigned properties are the file metadata plugin's job.
  HEIF and AVIF are not claimed at all.
- Tags are whatever portable carriers the container holds. A tool that stores keywords only in maker notes
  leaves them uncached.
- EXIF local time carries no zone unless `OffsetTimeOriginal` is present. When it is absent the value is
  stored with a zero offset rather than being guessed from the host's time zone.
- Animated WebP and APNG report a frame count but, like GIF, are previewed by the first frame only.
- Thumbnails are a single fixed size. There is no per-view-mode thumbnail set; the browser scales what is
  stored.
- A legacy raster source has to fit inside the host's per-provider source budget (16 MiB by default) to be processed at
  all, because the plugin buffers it before handing it to a native decoder. Raising
  `MediaExecutionLimits.SourceBytes` raises that ceiling; there is no partial-image decode.
- Decoding is in-process. A malformed image that crashes the native decoder would take the host with it; the
  host's timeout and failure isolation cover hangs and exceptions, not native faults.

## Container formats

The four format groups each expose an independently discovered metadata and preview capability. The
host schedules these previews during indexing so they can be retrieved with the source disconnected.
RAW publishes `skycd.image.raw.metadata` v1. Photoshop, vector and paint documents share
`skycd.image.document.metadata` v1. All four preview providers use `skycd.image.container.preview` v1.

| Group | Extensions | Metadata and preview |
|---|---|---|
| RAW | `.3fr .arw .cr2 .crw .crx .dng .gpr .kdc .nef .orf .pef .raf .rw2 .rwl .srw` | EXIF geometry, orientation, camera and exposure fields, capture date and tags. Largest bounded embedded JPEG, followed by the LibRaw thumbnail exposed by Magick.NET. |
| Photoshop | `.psd .psb` | Header geometry, color mode and bit depth, layer count and available embedded metadata. Photoshop thumbnail resources 1036 and 1033 take precedence over a Magick.NET composite. |
| SVG | `.svg .svgz` | Width and height in SVG user units, with viewBox dimensions as a fallback. Title, description and embedded RDF/XMP. Svg.Skia renders a bounded thumbnail from the document. |
| Illustrator | `.ai` | PdfPig reads PDF document properties and page dimensions in points, and PDFium renders the first page. For legacy PostScript, DSC supplies properties. |
| EPS | `.eps .epsf .epsi` | DSC reads title, creator, creation date and bounding box in points. |
| Metafiles | `.wmf .emf` | Header geometry in twips when the physical frame is present. Magick.NET renders a preview when its native build exposes a usable reader. |
| GIMP | `.xcf` | Header geometry, version, base type, layer count and the comment parasite. Magick.NET reads the flattened composite. Its coverage of newer GIMP features varies. |
| Krita | `.kra` | Archive XML supplies size, title and description. SkiaSharp decodes `preview.png` or `mergedimage.png`. |
| OpenRaster | `.ora` | Stack XML supplies size, name and layer count. SkiaSharp decodes `Thumbnails/thumbnail.png` or `mergedimage.png`. |
| Clip Studio Paint | `.clip` | The embedded SQLite database supplies size where the Canvas columns exist and PNG bytes from `CanvasPreview.ImageData`. |
| Affinity | `.afphoto .afdesign .afpub .aftemplate` | The largest complete embedded PNG supplies pixel size and the preview. |

CR3 is unclaimed pending a fixture that demonstrates both capture date and preview. Corel formats,
InDesign, HEIF and AVIF are outside this capability set. EPS and legacy Illustrator are properties-only.
Vector previews run offline and can omit content referenced by external resources or CSS that would load
those resources. CLIP and Affinity are best-effort because their container layouts are undocumented.

### Container resource limits

The default host budget is 16 MiB **per provider**, cumulative across reads. Container readers observe the
remaining budget exposed by `IMediaSource`. A 20 MiB RAW can still contribute metadata and an early
embedded JPEG. Decode fallbacks require the complete source to fit both the configured host budget and
the 128 MiB decoder cap. A provider can finish with an empty artifact list or partial metadata.

| Read | Maximum |
|---|---|
| RAW and Photoshop header/metadata | 4 MiB |
| RAW embedded JPEG search | 32 MiB |
| Candidate JPEG or PNG | 8 MiB |
| Uncompressed ZIP entry | 8 MiB |
| CLIP scan and SQLite slice, Affinity PNG scan | 64 MiB |
| EPS and legacy AI DSC | 64 KiB |
| XCF property walk | 1 MiB |
| Buffered composite decode or PDF-based AI | 128 MiB |

KRA and ORA seek to the ZIP directory and read selected entries. CLIP copies the pages identified by the
SQLite header to a temporary file, opens it read-only with connection pooling disabled, and deletes the
file in a `finally` block. SVGZ uses a bounded XML reader after decompression. Readers check cancellation
between source reads.

The Magick.NET path applies process-wide limits at capability startup. Memory is 256 MiB, map is 512 MiB,
disk is zero, width and height are limited to 8192, and native processing time is limited to 30 seconds.
The decoder uses a format allowlist. SVG belongs to Svg.Skia, PDF-based AI belongs to PDFium, and
PostScript parsing uses DSC without Ghostscript.

### Decoder dependencies and platform coverage

| Package | Windows | Linux | macOS | Responsibility |
|---|---|---|---|---|
| SkiaSharp and platform NativeAssets 3.119.4 | x64, arm64 | x64, arm64, musl | x64, arm64 | Raster and embedded previews, orientation and artifact encoding |
| Svg.Skia 4.9.1 and HarfBuzzSharp 8.3.1.3 | x64, arm64 | x64, arm64 | x64, arm64 | SVG/SVGZ rendering |
| Magick.NET-Q8-AnyCPU 15.1.1 | x64, arm64, x86 | x64, arm64, musl | arm64 | PSD/PSB and XCF composites, RAW thumbnail fallback, available metafile coders |
| Docnet.Core 2.6.0 | x64, x86 | x64, arm, arm64 | x64, arm64 | PDF-based AI first-page rendering |
| Microsoft.Data.Sqlite 11.0.0 and SQLite native dependencies | x64, arm64, x86 | x64, arm64, arm | x64, arm64 | Read-only CLIP preview and canvas query |
| MetadataExtractor 2.9.3, PdfPig 0.1.16, System.IO.Compression | managed | managed | managed | Metadata and container access |

Magick.NET's AnyCPU package omits macOS x64 natives. On that architecture its formats retain their
metadata or embedded previews. A metafile stays claimed when its native coder is absent. The native
package's upstream notice, including bundled dependencies such as LibRaw, is copied with the plugin.
The repository license gate reads package licenses from locked restores under the Trivy category policy.


## Testing

`tests/SkyCD.Plugin.Metadata.Image.Tests/` builds fixtures in memory with SkiaSharp or hand-assembled
container bytes. The single checked-in XCF fixture contains one synthetic layer and verifies native
compositing. Coverage includes a rotated JPEG with injected EXIF (orientation 2/3/6/8) checked at
pixel level, a transparent PNG, an animated GIF with a hand-written LZW stream, corrupt and truncated input,
a valid image whose EXIF directory is corrupt, oversized sources, cancellation, an exhausted host budget
surfacing as a failure rather than a crash, assembly discovery without a manifest, and offline retrieval
through the real scheduler and Couchbase Lite store with the database reopened and the plugin unloaded.
Tag fixtures cover JPEG XMP, IPTC and COM segments, PNG `tEXt` and XMP `iTXt` chunks, WebP `XMP ` chunks,
and a GIF asserting that no tag fields are published. `ContainerTagsPluginTests` hand-assembles TIFF
directories carrying the XP tags, `RatingPercent`, `Rating`, embedded XMP (tag 700) and IPTC (tag 33723),
and covers non-TIFF bytes under a `.tif` name, malformed and absurd directories, an empty source, the 1 MiB
prefix bound, cancellation, progress, and the descriptor claiming TIFF extensions only.
`EmbeddedMetadataReaderTests` assembles TIFF directories with an Exif sub-IFD to pin the date rules down —
`OffsetTimeOriginal` is applied to the value, `DateTimeOriginal` beats the IFD0 fallback, an unreadable
date produces no timestamp — and asserts that XMP wins the caption and rating while keywords from every
carrier merge, and that UCS-2 XP comments and big-endian `UserComment` text decode.
`ImagePluginIntegrationTests` also asserts that discovery finds all eleven capabilities from assembly
metadata alone.
