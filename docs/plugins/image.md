# Image preview and metadata plugin

Implements #438 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Image/` → plugin id `SkyCD.Plugin.Image`.
Tests: `tests/SkyCD.Plugin.Image.Tests/`.

## What it provides

Two independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Typed image metadata | `ImageMetadataPlugin : IMediaMetadataCapability` | `skycd.image.metadata` v1 |
| Bounded thumbnail | `ImagePreviewPlugin : IMediaPreviewCapability` | `skycd.image.preview` v1 |

Both claim extensions `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif` and MIME types `image/jpeg`, `image/png`,
`image/webp`, `image/gif`. Selection is by descriptor, so an unsupported type is never claimed and the host
records its own `unsupported` marker instead.

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

`Width`/`Height` are the oriented dimensions, so a 4000×3000 photo stored with orientation 6 reports
3000×4000 — the size a viewer shows. `Orientation` and `RotationDegrees` are published alongside so a host
can tell whether the numbers were already corrected.

Provenance is not a field: it is the record identity the host stores (plugin id, plugin version, capability
type, schema id, schema version), per `docs/architecture/media-cache-lifecycle.md`. A capture date is never
invented — an absent EXIF date produces no `CapturedAt` value rather than a fallback to the file timestamp.

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

The plugin reads through the host's bounded `IMediaSource` and never sees a path:

- `MediaSourceStream` adapts bounded random-access reads into the seekable stream a decoder needs. Every
  fetched byte is retained, so the decoder and the EXIF reader can both scan the source while the cumulative
  charge against the host budget stays at the high-water mark. Reads are fetched in 64 KiB blocks, so a
  byte-at-a-time metadata scan does not become thousands of host calls.
- `NonClosingStream` shields the adapter from libraries that dispose the stream they are handed — Skia
  disposes whatever stream a codec was created from.
- Metadata extraction reads only as far as the header and EXIF blocks; it does not decode pixels.
- A source above `ImagePluginOptions.MaxSourceBytes` (64 MiB, deliberately above the host's 16 MiB default)
  is rejected before a single byte is charged, so the host budget is what normally binds.
- Cancellation is checked before the first read, after decode and before encode. Decode itself is not
  interruptible; the host's per-provider timeout covers a decoder that ignores cancellation.
- Malformed and truncated sources raise `ImageDecodeException`, which the host turns into a `failed` record
  with a sanitized reason. Failures never surface plugin exception text to the catalog.

## Dependencies and licensing

| Package | Version | License | Why |
|---|---|---|---|
| `SkiaSharp` | 3.119.4 | MIT | Decode, orientation-aware scaling, thumbnail encoding |
| `SkiaSharp.NativeAssets.Win32` / `.Linux` / `.macOS` | 3.119.4 | MIT | Platform natives |
| `MetadataExtractor` | 2.9.3 | Apache-2.0 | EXIF capture date and UTC offset, which the decoder does not expose |

All are on the allow-list in `docs/legal/dependency-license-policy.json`. SkiaSharp 3.119.4 is the version
Avalonia already resolves for the desktop app, so the plugin adds no second imaging stack. `SixLabors.ImageSharp`
was rejected because its Six Labors Split License is not on the allow-list, and `TagLib#`-style LGPL options
are blocked outright.

## Limitations

- JPEG, PNG, WebP and GIF only. TIFF, HEIF, AVIF and RAW are not claimed; Skia can decode some of them but
  they are out of the documented initial scope.
- EXIF local time carries no zone unless `OffsetTimeOriginal` is present. When it is absent the value is
  stored with a zero offset rather than being guessed from the host's time zone.
- Animated WebP and APNG report a frame count but, like GIF, are previewed by the first frame only.
- Thumbnails are a single fixed size. There is no per-view-mode thumbnail set; the browser scales what is
  stored.
- Decoding is in-process. A malformed image that crashes the native decoder would take the host with it; the
  host's timeout and failure isolation cover hangs and exceptions, not native faults.

## Testing

`tests/SkyCD.Plugin.Image.Tests/` builds every fixture in memory with SkiaSharp or from hand-assembled
container bytes, so the suite carries no binary blobs and each fixture's properties are known to the test
that asserts on them. Coverage includes a rotated JPEG with injected EXIF (orientation 2/3/6/8), a
transparent PNG, an animated GIF with a hand-written LZW stream, corrupt and truncated input, oversized
sources, cancellation, the host's own source budget, assembly discovery without a manifest, and offline
retrieval through the real scheduler and Couchbase Lite store with the database reopened and the plugin
unloaded.
