# Video metadata and poster-frame plugin

Implements #440 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Video/` → plugin id `SkyCD.Plugin.Video`.
Tests: `tests/SkyCD.Plugin.Video.Tests/`.

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
- Producing a **poster frame** is a third, independent question, answered by whether a decoder binary is
  available. It usually is not, and that is a normal outcome.

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

Producing a frame needs a video decoder, and no license-compatible managed decoder covers H.264, HEVC, VP9 or
AV1, so the work is delegated to an **external, operator-supplied** tool through `IVideoFrameExtractor`.
`FFmpegFrameExtractor` is the shipped implementation.

Discovery order, and nothing else:

1. `VideoPluginOptions.FFmpegPath` — an explicit path.
2. `SKYCD_FFMPEG_PATH` — an environment variable.
3. A plain `PATH` lookup for `ffmpeg` / `ffmpeg.exe`.

**Nothing is ever downloaded**, no codec pack is fetched, and no bundled binary is assumed. This matters
because FFmpeg builds are not license-equivalent: a GPL build with `--enable-libx264` and an LGPL build
impose different obligations, so which binary runs is the operator's decision. If a configured path does not
exist, the extractor reports itself unavailable rather than falling back to a search.

When no decoder is available, when the source is larger than `MaxPosterSourceBytes`, or when the decoder
produces nothing, the capability returns an **empty artifact list**. The metadata result stands on its own,
the host stores a captured record with no artifact, and the browser shows its honest "no preview stored"
state. A missing decoder is not a failure.

Command construction seeks **after** the input (`-i pipe:0 -ss <t>`), because the source arrives over a pipe
and cannot be seeked; bounds the frame with
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

None. All three container families are parsed in the plugin, so there is no transitive dependency whose
license the policy gate has to judge. `FFmpeg.AutoGen` was rejected as LGPL, and wrapper packages that
redistribute FFmpeg binaries were rejected because the binary's license depends on its build
configuration — exactly the assumption the issue warns against.

## Limitations

- MP4/MOV, Matroska/WebM and AVI only. MPEG-TS, FLV, WMV and 3GP are not claimed.
- Poster frames require an operator-supplied decoder and a source small enough to pipe. Large files are
  always metadata-only; there is no partial or thumbnail-track fallback.
- Frame rate comes from the container's declared timing. Variable-frame-rate sources report the declared
  average, and an MP4 whose `stts` table has many entries reports the first entry's rate.
- Rotation is read from the container. A video whose rotation lives only in a bitstream-level SEI message is
  reported as upright.
- Subtitle, chapter and attachment tracks are walked past but not published; the schema covers video and
  audio only.
- Bitrate is a whole-file average derived from duration, not a per-stream measurement.

## Testing

`tests/SkyCD.Plugin.Video.Tests/` builds container-accurate fixtures in memory — a full MP4 atom tree with a
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
