# Image preview and metadata plugin

Implements #438 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Image/` → plugin id `SkyCD.Plugin.Image`.
Tests: `tests/SkyCD.Plugin.Image.Tests/`.

## What it provides

Three independently discovered capabilities in one assembly:

| Capability | Type | Schema | Claims |
|---|---|---|---|
| Typed image metadata and embedded tags | `ImageMetadataPlugin : IMediaMetadataCapability` | `skycd.image.metadata` v2 | `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`; `image/jpeg`, `image/png`, `image/webp`, `image/gif` |
| Bounded thumbnail | `ImagePreviewPlugin : IMediaPreviewCapability` | `skycd.image.preview` v1 | same as metadata |
| Tags from TIFF and TIFF-based RAW containers | `ContainerTagsPlugin : IMediaMetadataCapability` | `skycd.image.tags` v1 | `.tif`, `.tiff`, `.dng`, `.cr2`, `.nef`, `.arw`; `image/tiff` |

Selection is by descriptor, so an unsupported type is never claimed and the host records its own
`unsupported` marker instead. The tags-only capability never decodes pixels, so it can claim TIFF-based RAW
files that the decoder cannot present.

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
`SkyCD.Plugin.FileMetadata` (`docs/plugins/file-metadata.md`); each carrier is claimed once.

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

Compressed PNG text (`zTXt`, compressed `iTXt`) is not inflated: decompression is left to dedicated
editors rather than paid in an index pass. XMP provides `dc:subject`, `pdf:Keywords`,
`dc:description`, `exif:UserComment`, `xmp:Rating` and `MicrosoftPhoto:Rating`; IPTC-IIM provides
2:25 keywords and 2:120 caption; TIFF ratings use the Shell 1–99 scale, negative values mean
"rejected" and are ignored. The parsers are shared source files from `Plugins/Shared/MediaMetadata/`,
compiled into this assembly and into the XMP sidecar plugin, which needs the same XMP and IPTC code.

## Tags from TIFF and TIFF-based RAW

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
blobs, which are parsed with the same readers as the JPEG path. Maker notes are not parsed: they are
per-vendor and not portable. The scan reads at most the first 1 MiB of the source, in 256 KiB chunks and
through the host's budget, so a RAW file is never buffered whole. Damaged directories contribute nothing
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
consequence is that a source must fit inside the host's source budget to be processed at all, which is the
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
- A malformed EXIF directory is not a failure. Every offset is bounds checked and the reader gives up on
  tags while the geometry already extracted from the decoder stands.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `SkiaSharp` | 3.119.4 | MIT | Decode, orientation-aware scaling, thumbnail encoding |
| `SkiaSharp.NativeAssets.Win32` / `.Linux` / `.macOS` | 3.119.4 | MIT | Platform natives |

SkiaSharp is the only dependency, and 3.119.4 is the version Avalonia already resolves for the desktop app,
so the plugin adds no second imaging stack. All packages are on the allow-list in
`docs/legal/dependency-license-policy.json`, verified with the same CycloneDX SBOM the CI license gate uses.

EXIF is read by `ExifReader` directly from the container's TIFF directory rather than through a metadata
library. The obvious candidate (`MetadataExtractor`, Apache-2.0) drags in `XmpCore`, whose NuGet license is
declared as a URL rather than an SPDX expression; the policy gate reports it as unknown and
`failOnUnknownLicense` rejects it. Only two tags are actually needed — `DateTimeOriginal` with its
`OffsetTimeOriginal` companion, falling back to IFD0 `DateTime` — so parsing them in-plugin is both smaller
and license-clean. `SixLabors.ImageSharp` was likewise rejected: its Six Labors Split License is not on the
allow-list.

## Limitations

- JPEG, PNG, WebP and GIF are decoded. TIFF, HEIF, AVIF and RAW are never decoded; TIFF-based containers
  are claimed by the tags-only capability, and OS-assigned properties are the file metadata plugin's job.
  HEIF and AVIF are not claimed at all.
- Tags are whatever portable carriers the container holds. A tool that stores keywords only in a format
  the reader does not parse (compressed PNG text, maker notes) leaves them uncached.
- EXIF local time carries no zone unless `OffsetTimeOriginal` is present. When it is absent the value is
  stored with a zero offset rather than being guessed from the host's time zone.
- Animated WebP and APNG report a frame count but, like GIF, are previewed by the first frame only.
- Thumbnails are a single fixed size. There is no per-view-mode thumbnail set; the browser scales what is
  stored.
- A source has to fit inside the host's per-provider source budget (16 MiB by default) to be processed at
  all, because the plugin buffers it before handing it to a native decoder. Raising
  `MediaExecutionLimits.SourceBytes` raises that ceiling; there is no partial-image decode.
- Decoding is in-process. A malformed image that crashes the native decoder would take the host with it; the
  host's timeout and failure isolation cover hangs and exceptions, not native faults.

## Testing

`tests/SkyCD.Plugin.Image.Tests/` builds every fixture in memory with SkiaSharp or from hand-assembled
container bytes, so the suite carries no binary blobs and each fixture's properties are known to the test
that asserts on them. Coverage includes a rotated JPEG with injected EXIF (orientation 2/3/6/8) checked at
pixel level, a transparent PNG, an animated GIF with a hand-written LZW stream, corrupt and truncated input,
a valid image whose EXIF directory is corrupt, oversized sources, cancellation, an exhausted host budget
surfacing as a failure rather than a crash, assembly discovery without a manifest, and offline retrieval
through the real scheduler and Couchbase Lite store with the database reopened and the plugin unloaded.
Tag fixtures cover JPEG XMP, IPTC and COM segments, PNG `tEXt` and XMP `iTXt` chunks, WebP `XMP ` chunks,
and a GIF asserting that no tag fields are published. `ContainerTagsPluginTests` hand-assembles TIFF
directories carrying the XP tags, `RatingPercent`, `Rating`, embedded XMP (tag 700) and IPTC (tag 33723),
and covers non-TIFF bytes under a `.tif` name, malformed and absurd directories, an empty source, the 1 MiB
prefix bound, cancellation, progress, and the descriptor claiming TIFF and RAW extensions only.
`ImagePluginIntegrationTests` also asserts that discovery finds all three capabilities from assembly
metadata alone.
