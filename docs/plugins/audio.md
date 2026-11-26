# Audio tags and cover art plugin

Implements #439 (part of #424) on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).
The tag and stream parsing runs on TagLibSharp since #1223; the earlier hand-written container readers were
removed after the fixtures showed identical catalog values.

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

**No playback is involved.** Only tag blocks and container headers are parsed; audio samples are never
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
| `Codec` | Text | – | `mp3`, `flac`, `vorbis`, `aac`, `alac`, `opus`, `speex`, or the raw four-character code |
| `Container` | Text | – | `mp3`, `flac`, `ogg` or `mp4`, detected from the bytes |
| `HasCoverArt` | Boolean | – | Whether embedded art exists, independent of whether it was stored |
| `TagSource` | Text | – | `id3v2.3`, `id3v2.4`, `id3v1`, `vorbis-comment`, `itunes-atom`, `ape` or `none` |

### Explicit empty states

A field the source does not carry is **omitted**, never published as an empty string or a zero. That keeps
"unknown" distinguishable from "none" in the catalog, in the browser's preview pane and in search — a track
with no disc number simply has no `DiscNumber` field. `Container`, `HasCoverArt` and `TagSource` are always
known and therefore always present; `TagSource` is `none` when no tag block was found at all.
TagLibSharp reports zero for facts a container does not record (an MP3 has no bit depth), so the plugin
publishes nothing for them rather than a misleading `BitDepth: 0`.

### Multi-value tags

The host schema stores one value per key, so repeated fields are merged into one display string joined by
`"; "` with duplicates dropped. This covers Vorbis comments that repeat a key, several ID3v2 frames with the
same identifier and values separated inside one frame. TagLibSharp keeps only the first frame of a repeated
ID3v2 identifier, so the plugin reads repeated `TIT2`, `TPE1`, `TALB` and `TPE2` frames itself before the
tag-level values fill in the rest. Where ID3v2 and another scheme both carry a field, ID3v2 wins and the
other scheme only adds genuinely new values, so reading a tail tag refines rather than replaces the header.

### Tag provenance

`TagSource` names the scheme that is actually stored on disk, not the union TagLibSharp exposes in memory:
ID3v2 (with its major version), APEv2, iTunes atoms, Vorbis comments, then ID3v1, in that priority order.
`ape` is new with #1223 and reports APEv2 items read through TagLibSharp.

## Per-format support and limitations

| Container | Tags | Cover art | Duration | Notes |
|---|---|---|---|---|
| MP3 | ID3v2.2/2.3/2.4, ID3v1/1.1 tail, APEv2 | `APIC` (v2.3/2.4), `PIC` (v2.2), APEv2 `Cover Art (Front)` | Xing/Info or VBRI when present, else a constant-bitrate estimate | All four ID3v2 text encodings decoded |
| FLAC | Vorbis comments, APEv2 | `PICTURE` block | Exact, from `STREAMINFO` total samples | Also reports bit depth |
| Ogg Vorbis | Vorbis comments | `METADATA_BLOCK_PICTURE`, legacy `COVERART` | Exact, from the final page's granule position | Nominal bitrate preferred over a file-size average |
| M4A / MP4 | iTunes `ilst` atoms | `covr` atom | Exact, from `mvhd` (else the audio track's `mdhd`) | Codec from the sample entry (`mp4a` → `aac`, `alac` → `alac`, other four-character codes raw) |

Documented limitations:

- **Text encodings.** ID3v2 declares ISO-8859-1, UTF-16 with or without a byte order mark, and UTF-8; all
  four are decoded per value. ID3v1 is Latin-1 by definition and cannot represent characters outside it.
- **Genres.** Numeric ID3v1 genre indices and the `(17)` / `(CR)Refinement` forms are resolved through
  TagLibSharp's genre table, which covers the Winamp extension indices as well as the original 0–132 set.
  The plugin no longer keeps its own truncated table, so a file that declares a Winamp-only index now shows
  that genre's name instead of the raw number.
- **Duration without a VBR header.** An MP3 that has neither Xing/Info nor VBRI gets a constant-bitrate
  estimate from the audio byte count, which is approximate for genuine VBR streams.
- **Codecs.** The container is read whatever the codec inside is. An unrecognized MP4 sample entry still
  yields duration and tags with `Codec` reported as the raw four-character code. Ogg Opus and Speex streams
  are parsed by TagLibSharp and report their codec names, but only Ogg Vorbis is covered by the fixtures.
- **`.mp4` is deliberately not claimed.** That extension belongs to the video plugin (#440). An M4A has to be
  catalogued with an audio extension or the `audio/mp4` MIME type.

## Cover art handling

Embedded pictures are passed through **exactly as stored** — no re-encoding, no resizing, no imaging
dependency. A front cover is preferred when a source embeds several pictures, and the MIME type the
container declared is kept; bytes with no declared type are identified from their signature.

Art above `AudioPluginOptions.MaxCoverArtBytes` (4 MiB, matching the host's artifact budget) produces no
artifact rather than a failure or a truncated image, while `HasCoverArt` still reports `true`. That gives the
browser the honest state: art exists but was not stored. A source with no picture produces an empty artifact
list, which is the explicit "no cover art" state.

## Resource behaviour

TagLibSharp reads synchronously from a seekable stream, so `AudioSourceFile` adapts the host's bounded
`IMediaSource` into one: TagLib's positional reads become exact host range reads, and only the bytes a
container actually needs are charged. Tags at the head and tail of a 500 MB FLAC therefore cost a few
hundred kilobytes of budget instead of the whole file; a test asserts that reading an 8 MiB FLAC charges
under a quarter of the file. Requests are clamped to the source length, since the host rejects a range that
reaches past the end. Completion is blocked on the plugin's background thread, which has no
synchronization context, and a read that exhausts the host budget surfaces the host's own exception
unchanged on this plugin's stack, where it becomes an ordinary provider failure.

Detection is content-first: a file catalogued with the wrong extension is still read correctly, and bytes
that match no supported container raise `AudioDecodeException` rather than being reported as an empty
result. Corrupt and unsupported structures reported by TagLibSharp, and the plain length/range exceptions
its readers throw on malformed blocks, are converted to `AudioDecodeException`; truncated files that
TagLibSharp can still parse return the values it found. Cancellation is checked before the first read and
inside every stream read.

## Dependencies

TagLibSharp 2.3.0 (`TagLibSharp` on NuGet) supplies all tag and stream parsing. It is licensed
**LGPL-2.1-only** and is accepted under the #925 allowance for unmodified weak-copyleft assemblies, which
#1237 encoded as a category rule — LGPL-2.1 passes as Reciprocal in `trivy.yaml`, with no per-package entry,
and the policy forbids reintroducing one. Attribution, the license link and the full LGPL-2.1 text ship in
`src/SkyCD.App/THIRD-PARTY-NOTICES.txt` and `src/SkyCD.App/Licenses/LGPL-2.1.txt`.

SkyCD publishes framework-dependent builds without single-file bundling or trimming, so `TagLibSharp.dll`
stays a separate, unmodified assembly beside the plugin and can be replaced by the operator, which is what
the LGPL allowance rests on.

**Maintenance risk.** TagLibSharp's latest release, 2.3.0, dates from 2022 and the upstream project moves
slowly. The risk is bounded: TagLibSharp sits behind `TagLibAudioProber`, which maps its values onto the
catalog schema, and the fixture suite pins every published value per format. Replacing or patching the
library does not change the schema, and the separate DLL can be swapped without rebuilding SkyCD. The
previous hand-written readers were removed because they duplicated several thousand lines of container
parsing for the same fields TagLibSharp already reports.

## Testing

`tests/SkyCD.Plugin.Metadata.Audio.Tests/` builds container-accurate fixtures in memory — real ID3v2 frames with each
of the four text encodings, MPEG frame headers with and without a Xing header, an APEv2 tag in the
header/items/footer layout TagLibSharp writes, FLAC metadata blocks, Ogg pages with correct segment tables,
and a full MP4 atom tree — so the suite commits no binary blobs and every expected value is known to the
test that asserts it. Coverage includes tag and stream extraction per format, multi-value merging of
repeated ID3v2 frames, ID3v1 fallback, APE provenance and fields, numeric genre resolution, duration from
Xing and from an Ogg granule position, explicit empty states, cover-art pass-through for all four containers
in all four ID3v2 text encodings, oversized art withheld while `HasCoverArt` stays true, non-audio and
header-only bytes failing rather than reading as empty, cancellation, the host's own source budget, bounded
head/tail reads, assembly discovery without a manifest, and offline retrieval through the real scheduler and
Couchbase Lite store with the database reopened and the plugin unloaded.
