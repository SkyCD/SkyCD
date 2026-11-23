# Video metadata and poster-frame plugin

Implements #440 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Video/` → plugin id `SkyCD.Plugin.Metadata.Video`.
Tests: `tests/SkyCD.Plugin.Metadata.Video.Tests/`.

## What it provides

Two independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Container metadata | `VideoMetadataPlugin : IMediaMetadataCapability` | `skycd.video.metadata` v1 |
| Poster frame | `VideoPosterPlugin : IMediaPreviewCapability` | `skycd.video.preview` v1 |

Both claim extensions `.mp4`, `.m4v`, `.mov`, `.mkv`, `.webm`, `.avi` and MIME types `video/mp4`,
`video/quicktime`, `video/x-matroska`, `video/webm`, `video/x-msvideo`, `video/avi`.

`.m4a` is deliberately **not** claimed: that extension is the audio plugin's M4A entry point (#439). A plain
`.mp4` carrying audio only is still claimed here, because the container is a video container; the audio
plugin reaches M4A audio through `.m4a`/`.m4b` or `audio/mp4`.

Discovery uses assembly metadata and implemented capability types only. There is no `plugin.json`, and a
test asserts none exists.

## Container recognition is separate from codec availability

This is the central design rule of the plugin, and it is what lets a cataloguer read a file whose video
codec this machine could never play.

- The **container** is parsed in managed code with no decoder involved. If the atom tree, EBML element tree
  or RIFF chunk list is readable, metadata is produced.
- The **codec** is published as a normalized name where one is known (`h264`, `hevc`, `vp9`, `av1`, `aac`,
  `opus`, …) and as the raw four-character code or Matroska codec id otherwise. An unrecognized codec is
  never a failure.
- Producing a **poster frame** is a third, independent question, answered by whether the bundled decoder can
  read the source, or — when it cannot — whether a fallback binary is available. A missing poster is a normal
  outcome, not a metadata failure.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | `mp4`, `mov`, `mkv`, `webm` or `avi`, detected from the bytes |
| `Duration` | Number | `s` | Playback length |
| `Width` / `Height` | Integer | `px` | Display size; Matroska prefers `DisplayWidth/Height` over pixel size |
| `RotationDegrees` | Integer | `deg` | Clockwise rotation, 0–359 |
| `VideoCodec`, `AudioCodec` | Text | – | Normalized where known, raw identifier otherwise |
| `FrameRate` | Number | `fps` | From the video track's timescale and sample delta |
| `Bitrate` | Integer | `kbps` | Average over the file, derived from duration |
| `VideoStreamCount`, `AudioStreamCount` | Integer | – | Tracks of each kind |
| `Title` | Text | – | Container-level title where the format stores one |

`RotationDegrees` is published when the container declares one, **including zero** — an explicit "upright"
is information. Containers with no rotation concept (AVI) omit the field rather than claiming zero, so
"unknown" and "upright" stay distinguishable. Every other absent field is omitted, never zero-filled.

Rotation sources differ per container and are read from the authoritative place in each: the QuickTime track
transformation matrix for MP4/MOV (stored row-major as `a, b, u, c, d, v, x, y, w`), and
`Projection/ProjectionPoseRoll` for Matroska. Only the four axis-aligned rotations are recognized; an
arbitrary transform is reported as upright rather than approximated.

## Poster frames

Two decoders are tried in order through `IVideoFrameExtractor`:

1. `OpenCvFrameExtractor` — decodes in-process with the OpenCV videoio backend bundled with the plugin. It
   covers the codecs these containers carry (H.264, HEVC, VP8, VP9, AV1, MJPEG), so a fresh install stores a
   poster with **no external tool**.
2. `FFmpegFrameExtractor` — an optional, operator-supplied FFmpeg binary, used only when OpenCV produced no
   frame. It remains the poster source on platforms without the Windows native runtime and for codecs
   OpenCV's bundled build cannot decode.

`CompositeVideoFrameExtractor` holds both and is the default extractor; a successful OpenCV frame never starts
the fallback. Discovery of the fallback is unchanged and nothing is ever downloaded:

1. `VideoPluginOptions.FFmpegPath` — an explicit path.
2. `SKYCD_FFMPEG_PATH` — an environment variable.
3. A plain `PATH` lookup for `ffmpeg` / `ffmpeg.exe`.

**Nothing is ever downloaded**, no external codec pack is fetched, and no unbundled binary is assumed. The
fallback stays opt-in because FFmpeg builds are not license-equivalent: a GPL build with `--enable-libx264`
and an LGPL build impose different obligations, so which external binary runs is the operator's decision. If
a configured path does not exist, the extractor reports itself unavailable rather than falling back to a
search.

### OpenCV decoder details

- The pinned managed binding has no `Stream` constructor, so the already-bounded source is copied into a
  private temporary file that is deleted as soon as the frame is read. The copy is never larger than
  `MaxPosterSourceBytes`, and no second permanent copy of the media exists.
- `PosterTimestampSeconds` is clamped to the clip's duration (frame count / fps) and paired with a read; a
  seek that lands past the last decodable frame retries from frame zero.
- The frame is resized so its longest edge is at most `MaxPosterDimension` and encoded as JPEG. A frame above
  `MaxJpegBytes` is re-encoded at lower quality and dropped if it still exceeds the cap.
- Native work runs on a worker task bounded by `DecoderTimeoutMilliseconds`. A timed-out native call cannot be
  aborted; it is abandoned and cleans up its own temporary file when it returns.
- OpenCV is used for **decoding only**. `VideoCapture` does not expose title, audio codec, bitrate, stream
  counts or the container id, so the MP4/QuickTime/Matroska/AVI readers remain the metadata source of record.

When no decoder is available, when the source is larger than `MaxPosterSourceBytes`, or when both decoders
produce nothing, the capability returns an **empty artifact list**. The metadata result stands on its own,
the host stores a captured record with no artifact, and the browser shows its honest "no preview stored"
state. A missing or failing decoder is not a failure.

The FFmpeg fallback command seeks **after** the input (`-i pipe:0 -ss <t>`), because the source arrives over a
pipe and cannot be seeked; bounds the frame with
`scale=w='min(N,iw)':h='min(N,ih)':force_original_aspect_ratio=decrease`; emits one frame
(`-frames:v 1`) as JPEG to `pipe:1`; and leaves rotation to the decoder's own autorotation rather than
applying it twice.

## Resource behaviour

Metadata extraction reads **only headers**. Readers walk the atom, element or chunk tree reading fixed-size
headers and skipping payloads by offset, so the media payload is never touched. A test asserts that reading
metadata from a 4 MiB MP4 charges under 64 KiB, and that a non-faststart MP4 with a trailing `moov` box is
still read without streaming the file. This is what makes gigabyte-scale video work inside the host's 16 MiB
per-provider read budget: the budget charges bytes actually read, not bytes skipped.

Poster generation is the exception, because a decoder needs the actual media. It is therefore bounded
separately by `MaxPosterSourceBytes` (16 MiB by default, matching the host budget) and sources above it are
stored metadata-only. `HeaderScanBytes` bounds header scanning, `MaxPictureSearchAtoms` bounds atom visits so
a pathological tree cannot spin, and `DecoderTimeoutMilliseconds` bounds one decoder run on top of the host's
own per-provider timeout. Cancellation is checked before the first read and at every atom, element and chunk.

Malformed input raises `VideoDecodeException`, which the host records as a `failed` record with a sanitized
reason. Truncation is detected rather than tolerated: an `ftyp` box alone, an EBML header with no Segment, or
a RIFF header with no `hdrl` list are all failures, not empty results.

## Dependencies

`OpenCvSharp4` and `OpenCvSharp4.runtime.win` `4.13.0.20261107`, both Apache-2.0, provide the in-process
decoder. The runtime package bundles a dynamically loaded LGPL-2.1 FFmpeg videoio build; it is recorded in
`src/SkyCD.App/THIRD-PARTY-NOTICES.txt`. Trivy reads nuspec metadata, sees only the Apache-2.0 expression, and
the bundled DLL is visible only in the notices. The two native DLLs are linked next to `OpenCvSharp.dll` in the
plugin output, because plugin assemblies load with `Assembly.LoadFrom` and the host's `deps.json` never lists
them. All three container families remain parsed by the plugin itself, so no additional package is involved in
metadata extraction. `FFmpeg.AutoGen` was rejected as LGPL, and wrapper packages that redistribute FFmpeg
binaries outside an Apache-2.0 package were rejected because the binary's license depends on its build
configuration.

## Limitations

- MP4/MOV, Matroska/WebM and AVI only. MPEG-TS, FLV, WMV and 3GP are not claimed.
- Poster frames are bounded by `MaxPosterSourceBytes`; large files are always metadata-only, with no partial
  or thumbnail-track fallback.
- The bundled native runtime is Windows x64. Other platforms fall back to an operator-supplied FFmpeg binary
  or stay metadata-only.
- Frame rate comes from the container's declared timing. Variable-frame-rate sources report the declared
  average, and an MP4 whose `stts` table has many entries reports the first entry's rate.
- Rotation is read from the container. A video whose rotation lives only in a bitstream-level SEI message is
  reported as upright.
- Subtitle, chapter and attachment tracks are walked past but not published; the schema covers video and
  audio only.
- Bitrate is a whole-file average derived from duration, not a per-stream measurement.

## Testing

`tests/SkyCD.Plugin.Metadata.Video.Tests/` builds container-accurate fixtures in memory — a full MP4 atom tree with a
real transformation matrix and a trailing-`moov` variant, Matroska with EBML variable-width ids and sizes
including an unknown-size Segment, WebM, and a RIFF/AVI with `hdrl`, `strl`, `strh` and `strf` chunks — so
the suite commits no binary blobs and every expected value is known to the test asserting it.

Coverage includes duration, dimensions, rotation for all four axis-aligned orientations, codecs, frame rate,
stream counts and title per container; QuickTime versus MP4 brand detection; display-size preference in
Matroska; unknown codecs published raw; metadata-only results when no decoder is present; poster generation
through a stub decoder including exactly which bytes and bounds it receives; oversized sources skipped
without invoking the decoder; the host's own source budget and timeout around a slow decoder; non-video and
truncated bytes failing per extension; cancellation; header-only read budgets; assembly discovery without a
manifest; and offline retrieval through the real scheduler and Couchbase Lite store with the database
reopened and the plugin unloaded.

The in-process decoder is covered separately. `VideoFixtures.MjpegAvi` encodes JPEG frames at runtime and
wraps them in a real RIFF/AVI container, so the suite still commits no binary; on Windows the tests decode
that clip through OpenCV, check the `MaxPosterDimension` bound, prove an undecodable source returns no frame
rather than throwing, and store a poster through `VideoPosterPlugin` with no external FFmpeg at all. Composite
fallback ordering is covered platform-independently with stub extractors.
