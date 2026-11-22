# Audio tags and cover art plugin

Implements #439 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Audio/` → plugin id `SkyCD.Plugin.Metadata.Audio`.
Tests: `tests/SkyCD.Plugin.Metadata.Audio.Tests/`.

## What it provides

Two independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Normalized tags and stream facts | `AudioMetadataPlugin : IMediaMetadataCapability` | `skycd.audio.metadata` v1 |
| Embedded cover art | `AudioCoverArtPlugin : IMediaPreviewCapability` | `skycd.audio.preview` v1 |

Both claim extensions `.mp3`, `.flac`, `.ogg`, `.oga`, `.m4a`, `.m4b` and MIME types `audio/mpeg`,
`audio/mp3`, `audio/flac`, `audio/x-flac`, `audio/ogg`, `application/ogg`, `audio/mp4`, `audio/x-m4a`.
Discovery uses assembly metadata and implemented capability types only — there is no `plugin.json`, and a
test asserts none exists.

**No playback is involved.** Only container structures and tag blocks are parsed; audio samples are never
decoded and the original is never modified.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Title`, `Artist`, `Album`, `AlbumArtist`, `Genre` | Text | – | Normalized tag text |
| `TrackNumber`, `TrackCount` | Integer | – | Position and total from `n/m` style pairs |
| `DiscNumber`, `DiscCount` | Integer | – | Same, for the disc |
| `Year` | Integer | – | Four-digit year from a date or year field |
| `Duration` | Number | `s` | Playback length in seconds |
| `Bitrate` | Integer | `kbps` | Frame, nominal or average bitrate |
| `SampleRate` | Integer | `Hz` | Sample rate |
| `Channels` | Integer | – | Channel count |
| `BitDepth` | Integer | `bit` | Bits per sample, where the container records it |
| `Codec` | Text | – | `mp3`, `flac`, `vorbis`, `aac`, `alac`, or the raw four-character code |
| `Container` | Text | – | `mp3`, `flac`, `ogg` or `mp4`, detected from the bytes |
| `HasCoverArt` | Boolean | – | Whether embedded art exists, independent of whether it was stored |
| `TagSource` | Text | – | `id3v2.3`, `id3v2.4`, `id3v1`, `vorbis-comment`, `itunes-atom` or `none` |

### Explicit empty states

A field the source does not carry is **omitted**, never published as an empty string or a zero. That keeps
"unknown" distinguishable from "none" in the catalog, in the browser's preview pane and in search — a track
with no disc number simply has no `DiscNumber` field. `Container`, `HasCoverArt` and `TagSource` are always
known and therefore always present; `TagSource` is `none` when no tag block was found at all.

### Multi-value tags

The host schema stores one value per key, so repeated fields are merged into one display string joined by
`"; "` with duplicates dropped. This covers Vorbis comments that repeat a key, several ID3v2 frames with the
same identifier, and values separated inside one frame. Where two schemes both carry a field, ID3v2 wins and
ID3v1 only adds genuinely new values, so reading the tail refines rather than replaces the header tags.

## Per-format support and limitations

| Container | Tags | Cover art | Duration | Notes |
|---|---|---|---|---|
| MP3 | ID3v2.2/2.3/2.4, ID3v1/1.1 tail | `APIC` (v2.3/2.4), `PIC` (v2.2) | Xing/Info or VBRI when present, else a CBR estimate | All four ID3v2 text encodings decoded |
| FLAC | Vorbis comments | `PICTURE` block | Exact, from `STREAMINFO` total samples | Also reports bit depth |
| Ogg Vorbis | Vorbis comments | `METADATA_BLOCK_PICTURE`, legacy `COVERART` | Exact, from the final page's granule position | Nominal bitrate preferred over a file-size average |
| M4A / MP4 | iTunes `ilst` atoms | `covr` atom | Exact, from `mvhd` (else the audio track's `mdhd`) | `©nam ©ART ©alb aART ©gen gnre ©day trkn disk covr` |

Documented limitations:

- **Text encodings.** ID3v2 declares ISO-8859-1, UTF-16 with or without a byte order mark, and UTF-8; all
  four are decoded per value. ID3v1 is Latin-1 by definition and cannot represent characters outside it.
- **Genres.** Numeric ID3v1 genre indices and the `(17)` / `(CR)Refinement` forms are resolved through the
  canonical table. The table covers indices 0–132; higher Winamp-only indices are published as the raw text,
  because several of them are slurs this project will not embed.
- **Duration without a VBR header.** An MP3 that has neither Xing/Info nor VBRI gets a constant-bitrate
  estimate from the audio byte count, which is approximate for genuine VBR streams.
- **Codecs.** The container is read whatever the codec inside is. An unrecognized MP4 sample entry still
  yields duration and tags with `Codec` reported as the raw four-character code. Opus in Ogg is not parsed —
  the reader requires a Vorbis identification header.
- **`.mp4` is deliberately not claimed.** That extension belongs to the video plugin (#440). An M4A has to be
  catalogued with an audio extension or the `audio/mp4` MIME type.

## Cover art handling

Embedded pictures are passed through **exactly as stored** — no re-encoding, no resizing, no imaging
dependency. A front cover is preferred when a source embeds several pictures.

Art above `AudioPluginOptions.MaxCoverArtBytes` (4 MiB, matching the host's artifact budget) produces no
artifact rather than a failure or a truncated image, while `HasCoverArt` still reports `true`. That gives the
browser the honest state: art exists but was not stored. A source with no picture produces an empty artifact
list, which is the explicit "no cover art" state.

Because art is not re-encoded, the plugin adds no image dependency; #438's image plugin renders what the
browser receives.

## Resource behaviour

Readers ask for exact byte ranges through the host's bounded `IMediaSource` rather than a stream, so tags at
the head and tail of a 500 MB FLAC cost a few hundred kilobytes of budget instead of the whole file. A test
asserts that reading an 8 MiB FLAC's tags charges under a quarter of the file. Requests are clamped to the
source length, since the host rejects a range that reaches past the end, and a read that exhausts the host
budget surfaces the host's own exception on this plugin's stack, where it becomes an ordinary provider
failure.

Nothing here is native code, so unlike the image plugin there is no risk of an exception escaping through a
native callback.

`AudioPluginOptions` bounds the work: `HeaderScanBytes` (1 MiB) for locating the first MPEG frame,
`TagScanBytes` (4 MiB) per tag block, `MaxCoverArtBytes` (4 MiB), and `MaxPictureSearchAtoms` (512) as an
atom-visit cap so a pathological MP4 tree cannot spin. Cancellation is checked before the first read and at
every page, atom and metadata block.

Container detection is content-first: a file catalogued with the wrong extension is still read correctly, and
bytes that match no supported container raise `AudioDecodeException` rather than being reported as an empty
result. Truncated containers fail the same way — an `ftyp` box alone is not an audio file.

## Dependencies

None. Every container is parsed in the plugin, so there is no transitive dependency whose license the policy
gate has to judge. `TagLib#` was rejected as LGPL-2.1 and `z440.atl.core`, although MIT itself, pulls in
`Ude.NetStandard`, whose NuGet license is declared as a URL rather than an SPDX expression and which the gate
reports as unknown.

## Testing

`tests/SkyCD.Plugin.Metadata.Audio.Tests/` builds container-accurate fixtures in memory — real ID3v2 frames with each
of the four text encodings, MPEG frame headers with and without a Xing header, FLAC metadata blocks, Ogg
pages with correct segment tables, and a full MP4 atom tree — so the suite commits no binary blobs and every
expected value is known to the test that asserts it. Coverage includes tag and stream extraction per format,
multi-value merging, ID3v1 fallback, numeric genre resolution, duration from Xing and from an Ogg granule
position, explicit empty states, cover-art pass-through for all four containers, oversized art withheld while
`HasCoverArt` stays true, non-audio and truncated bytes failing rather than reading as empty, cancellation,
the host's own source budget, bounded head/tail reads, assembly discovery without a manifest, and offline
retrieval through the real scheduler and Couchbase Lite store with the database reopened and the plugin
unloaded.
