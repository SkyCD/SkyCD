# Audio tags, cover art and audio CD plugin

Implements #439 (part of #424) and #732 on the capability contracts from #435
(`docs/plugins/media-capabilities.md`). The tag and stream parsing runs on TagLibSharp since #1223; the
earlier hand-written container readers were removed after the fixtures showed identical catalog values.
Audio CD identification was consolidated into this assembly so one plugin covers both file-level audio
tags and mounted optical discs.

Assembly: `Plugins/SkyCD.Plugin.Metadata.Audio/` → plugin id `SkyCD.Plugin.Metadata.Audio`.
Tests: `tests/SkyCD.Plugin.Metadata.Audio.Tests/`.

## What it provides

Four independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Normalized tags and stream facts | `AudioMetadataPlugin : IMediaMetadataCapability` | `skycd.audio.metadata` v1 |
| Embedded cover art | `AudioCoverArtPlugin : IMediaPreviewCapability` | `skycd.audio.preview` v1 |
| Audio CD disc and track identification | `AudioCdMetadataPlugin : IMediaTargetMetadataCapability` | `skycd.audio-cd.disc` / `skycd.audio-cd.track` v1 |
| Audio CD Options page | `AudioCdConfigurationPlugin : IPluginConfigurationCapability` | `skycd.audio-cd.settings` |

The file-level capabilities claim extensions `.mp3`, `.flac`, `.ogg`, `.oga`, `.m4a`, `.m4b` and MIME types
`audio/mpeg`, `audio/mp3`, `audio/flac`, `audio/x-flac`, `audio/ogg`, `application/ogg`, `audio/mp4`,
`audio/x-m4a`. The audio CD capability claims mounted optical media and the shell's `.cda` track entries
through its `PrimaryExtension`.

Discovery uses assembly metadata and implemented capability types only — there is no `plugin.json`, and a
test asserts none exists. The assembly carries one plugin id and one icon; catalog file identity for the
plugin resolves from the first file-level capability (`mp3`), while the audio CD capability keeps `cda` for
the host's optical-disc routing.

**No playback is involved.** Only tag blocks, container headers and the disc's table of contents are read;
audio samples are never decoded and the original is never modified.

## Audio tags

### Metadata schema

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

#### Explicit empty states

A field the source does not carry is **omitted**, never published as an empty string or a zero. That keeps
"unknown" distinguishable from "none" in the catalog, in the browser's preview pane and in search — a track
with no disc number simply has no `DiscNumber` field. `Container`, `HasCoverArt` and `TagSource` are always
known and therefore always present; `TagSource` is `none` when no tag block was found at all.
TagLibSharp reports zero for facts a container does not record (an MP3 has no bit depth), so the plugin
publishes nothing for them rather than a misleading `BitDepth: 0`.

#### Multi-value tags

The host schema stores one value per key, so repeated fields are merged into one display string joined by
`"; "` with duplicates dropped. This covers Vorbis comments that repeat a key, several ID3v2 frames with the
same identifier and values separated inside one frame. TagLibSharp keeps only the first frame of a repeated
ID3v2 identifier, so the plugin reads repeated `TIT2`, `TPE1`, `TALB` and `TPE2` frames itself before the
tag-level values fill in the rest. Where ID3v2 and another scheme both carry a field, ID3v2 wins and the
other scheme only adds genuinely new values, so reading a tail tag refines rather than replaces the header.

#### Tag provenance

`TagSource` names the scheme that is actually stored on disk, not the union TagLibSharp exposes in memory:
ID3v2 (with its major version), APEv2, iTunes atoms, Vorbis comments, then ID3v1, in that priority order.
`ape` is new with #1223 and reports APEv2 items read through TagLibSharp.

### Per-format support and limitations

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

### Embedded cover art

Embedded pictures are passed through **exactly as stored** — no re-encoding, no resizing, no imaging
dependency. A front cover is preferred when a source embeds several pictures, and the MIME type the
container declared is kept; bytes with no declared type are identified from their signature.

Art above `AudioPluginOptions.MaxCoverArtBytes` (4 MiB, matching the host's artifact budget) produces no
artifact rather than a failure or a truncated image, while `HasCoverArt` still reports `true`. That gives the
browser the honest state: art exists but was not stored. A source with no picture produces an empty artifact
list, which is the explicit "no cover art" state.

### Resource behaviour

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

## Audio CD identification

An audio CD has no filesystem to read tags from, so identification and enrichment exist only at the volume
level. The capability lives under `AudioCD/` with namespaces rooted at `SkyCD.Plugin.Metadata.Audio.AudioCD`.

### Identification

The table of contents is read with the in-box facilities of each supported operating system; no COM interop,
native package or helper binary is involved:

| OS | Reader | Mechanism |
|---|---|---|
| Windows | `WindowsDiscTocReader` | MCI cdaudio device (`winmm.dll`, `mciSendStringW`): track count and per-track lengths, folded back into absolute CD frames from the standard 150-frame lead-in. MCI reports the last track one frame short, so the lead-out gains that frame back. |
| Linux | `LinuxDiscTocReader` | `CDROMREADTOCHDR` / `CDROMREADTOCENTRY` ioctls on the drive's device node (`/dev/srN`); a mounted disc is resolved through `/proc/self/mounts`. |
| macOS | `MacDiscTocReader` | IOKit (`IOCDMedia`) locates the media's BSD node, then `DKIOCCDREADTOC` returns the TOC in SCSI layout. Raw device access may require the operator group. |

`PlatformDiscTocReader` selects the reader for the current OS; an unsupported platform simply returns no
record. None of the readers reads the disc's data session.

The same TOC yields both identifiers the online sources use:

| Id | Algorithm |
|---|---|
| MusicBrainz disc id | SHA-1 over ASCII hex `FirstTrack(2) LastTrack(2)` then 100 `%08X` slots (lead-out first, then each track offset, zero-padded), Base64 with `+→.`, `/→_`, `=→-` |
| freedb/CDDB id | Digit sums of every track offset in whole seconds mod 0xFF, disc playing time in seconds and track count packed into one 32-bit value, lowercase hex |

Both are unit tested against published vectors (`49HHV7Eb8UKF3aQiNmu1GR8vKTY-` for the documented
MusicBrainz example TOC). The catalog records both ids as fields, so a disc can be looked up again later even
when the sources were offline during the scan.

Disc roots are found two ways, because a drive may be scanned either as a media root or as a plain folder:

- a folder/media entry whose properties carry a non-empty `RootDirectory` and `DriveType` of `CDRom` — a
  drive letter on Windows, a device node such as `/dev/sr0` or `/dev/rdisk4` on Linux and macOS;
- a `.cda` track file next to its siblings (the shell's virtual track files); the containing directory is
  the disc root.

Non-optical drives, folders and files are ignored. Track records attach to the `.cda` files directly under the
disc root, matched by their trailing number. Because an audio CD has no filesystem, the host also offers it
for scanning directly from its device node on Linux (sysfs, only drives with a readable disc that is not
mounted) and macOS (IOKit, media present); such a scan produces the media root, and its disc-level metadata
is captured even though there are no file entries.

### Audio CD schema

| Schema | Key | Type | Meaning |
|---|---|---|---|
| `skycd.audio-cd.disc` v1 | `Album` | Text | Release title |
| | `Artist`, `AlbumArtist` | Text | Release artist (only when disc artist tags are enabled) |
| | `Year` | Integer | Release year |
| | `Genre`, `Country`, `Label` | Text | When the source carries them |
| | `TrackCount` | Integer | Positions on the disc |
| | `MusicBrainzDiscId` | Text | Always computed |
| | `CddbDiscId` | Text | Always computed |
| | `Source` | Text | `MusicBrainz`, `GnuDb` or `none` |
| `skycd.audio-cd.track` v1 | `Title` | Text | Track title |
| | `Artist` | Text | Track artist (only when track artist tags are enabled) |
| | `Album` | Text | Release title |
| | `TrackNumber` | Integer | Position on the disc |
| | `Length` | Number (`s`) | From the TOC frame span |

A field the source does not carry is omitted, never published as an empty string, so "unknown" stays
distinguishable from "empty". When no source matches, the disc still records both ids, its track count and
the TOC-derived track lengths with the `none` source — identification succeeds even when enrichment does not.

### Sources and network behaviour

| Source | Endpoint | Limits |
|---|---|---|
| MusicBrainz WS/2 | `https://musicbrainz.org/ws/2/discid/{id}?fmt=json&inc=artists%2Brecordings` | ≤ 1 request/s, descriptive `User-Agent` |
| GnuDb | `https://gnudb.gnudb.org/~cddb/cddb.cgi` | ≤ 1 request/s |
| Cover Art Archive | `https://coverartarchive.org/release/{releaseId}/front[-250\|-500\|-1200]` | same client, no extra throttle |

Sources are tried in the configured order and the first match wins; a failing source falls through to the
next one. Each request has its own configurable timeout (default 8 s, 1–120 s allowed), response bodies are
bounded at 1 MiB, and cancellation is honoured end to end — including after a provider call, and while
waiting on a throttle.

Failures are never fatal to a scan:

| Condition | Result |
|---|---|
| Offline mode enabled | No network calls; disc/track records from the TOC only |
| Service unreachable, non-200 or timed out | That source is skipped; the next source is tried |
| Malformed response | Source skipped; the disc degrades to TOC-only |
| No matching release | Disc stays identified, source `none` |
| No artwork | Records are imported without a cover |
| TOC unreadable (empty drive, data disc) | No records; the scan continues |

### Audio CD cover art

The front cover is retrieved once at the larger of the two configured sizes and rendered twice, so disc and
track covers can differ in size without a second download. Size is bounded to 32–2048 px, default 250 px
(the issue's reference tile). Images are never upscaled: an image already within the requested edge is
passed through byte-for-byte with its original content type, and art bytes are withheld rather than
published broken when decoding fails.

### Settings

Enable the plugin to show its embedded **Options → Metadata → Audio CD** page. Navigation keeps the draft;
Options Save persists and applies it, while Cancel/close leaves stored settings unchanged.
See [SDK configuration contracts](sdk-contracts.md#embedded-options-configuration-sdk-20-migration-828).

Persisted through the plugin configuration store and applied on the next scan (no restart):

| Key | Default | Meaning |
|---|---|---|
| `sources` | `musicbrainz;gnudb` | Ordered, semicolon/comma separated; empty disables online lookup |
| `discArtistTags` | `true` | Write artist fields on the disc record |
| `trackArtistTags` | `true` | Write artist fields on track records |
| `discCovers` | `true` | Download and store a disc cover |
| `trackCovers` | `false` | Download and store per-track covers |
| `discCoverSize` | `250` | Target longest edge, px (32–2048) |
| `trackCoverSize` | `250` | Target longest edge, px (32–2048) |
| `offline` | `false` | Suppress all network access |
| `requestTimeoutSeconds` | `8` | Per-request timeout (1–120) |

Malformed values are clamped or fall back to defaults rather than failing the scan. Settings are read at
capture time by the host, so a change takes effect on the next scan without reloading the plugin.

### Host integration

The plugin never writes the catalog. Records are staged into the media cache batch the scanning view model
already manages, and published atomically with the file records:

- `IMediaTargetMetadataCapability` / `MediaTarget` / `MediaTargetCapture` — the plugin seam in the
  `SkyCD.SDK.Capabilities.Media` contract package;
- `MediaTargetExtensionManager` (`src/SkyCD.Plugin.Host/Media/`) — generic host manager: priority order,
  per-plugin settings resolution, failure isolation through `PluginFailure.Publish`, cancellation passthrough,
  registered as `IMediaTargetMetadataProvider`;
- `AudioCdMetadataService` (`src/SkyCD.Presentation/MediaCache/`) — platform-neutral disc-root discovery,
  artifact staging (`preview:0`), and per-entry fingerprints, mirroring the file metadata service;
- `SystemMediaDriveProvider` / `MediaCatalogScanner` — on Unix an audio CD exists only as a device node;
  sysfs and IOKit probes offer it for scanning, and the scanner catalogues it as a media root without
  traversing a filesystem;
- `DiscoveredPluginFactory` falls back to `IMediaTargetMetadataCapability.PrimaryExtension` when no file-level
  capability supplies an extension; here the metadata capability does, so the plugin icon identity stays
  `mp3` while the audio CD capability still advertises `cda`;
- `MainWindowViewModel.StageMetadataAsync` runs the disc step before the file loop and reports failures as a
  scan warning.

## Dependencies

`dotnet list Plugins/SkyCD.Plugin.Metadata.Audio/SkyCD.Plugin.Metadata.Audio.csproj package --include-transitive`:

| Package | Version | License | Use |
|---|---|---|---|
| TagLibSharp | 2.3.0 | LGPL-2.1 | Tag and stream parsing |
| SkiaSharp | 3.119.4 | MIT | Decode and downscale Cover Art Archive images |
| SkiaSharp.NativeAssets.Win32 / Linux / macOS | 3.119.4 | MIT | Native renderer for each RID |
| Avalonia, Avalonia.Desktop | 12.1.6 | MIT | Embedded settings page, same pattern as Sample.Configuration |

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

SkiaSharp and Avalonia are MIT dependencies that already pass the Trivy license gate in
`docs/legal/dependency-policy.md` and are already used elsewhere in the repo (`SkyCD.Plugin.Metadata.Image`
uses SkiaSharp; every UI plugin uses Avalonia), so no policy change is needed. Network and JSON use the MIT
.NET runtime (`HttpClient`, `System.Text.Json`); no HTTP or JSON package is added.

Alternatives considered for the audio CD lookup:

- **MetaBrainz.MusicBrainz.Client**: the maintained client for WS/2, but LGPL-3.0 — a blocked license family.
  The required lookup is one GET plus field extraction, so the BCL client replaces it.
- **TagLib#**: LGPL-2.1; also has nothing to read on an audio CD, which carries no tag blocks.
- **freedb libraries**: unmaintained; GnuDb's protocol is a single HTTP GET with a plain-text body.

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

The audio CD cases under `tests/SkyCD.Plugin.Metadata.Audio.Tests/AudioCD/` use no network and no binary blobs:

- disc-id vectors (published MusicBrainz example, hand-computed CDDB id) and invalid-TOC rejection;
- settings defaults, clamping, round-trip and editor normalisation;
- fake HTTP handler cases — matched, unmatched, 404/non-200, malformed JSON, slow-source timeout, caller
  cancellation, and measured throttle spacing — for MusicBrainz, GnuDb (including 211 multi-match and
  continuation lines) and Cover Art Archive;
- cover renderer: pass-through without upscaling, downscale to the requested edge, undecodable input;
- TOC readers: Linux mount-table resolution (longest mount, escaped names) and frame assembly, macOS CDTOC
  parsing (audio/data, truncated length, raw-device conversion), and the platform dispatcher — all driven by
  captured struct/buffer data, so no optical drive is needed in CI;
- plugin behaviour: disc/track field values, disc vs track tag gating, disc vs track cover sizes, cover
  downloads skipped when disabled, missing artwork, offline, unreachable-then-GnuDb fallback, cancellation,
  assembly discovery without a manifest;
- host manager: priority order, persisted settings delivery, failing-provider isolation, broken settings
  resolver, blank root, cancellation;
- catalog integration through the in-memory store and `MediaPreviewResolver`: optical-root and `.cda`
  inference, disc/track staging, offline read-back, unmatched and throwing providers.

## Known limitations

- **Audio-only discs.** Windows MCI ignores data tracks; the Linux and macOS readers reject a mixed-mode or
  CD-Extra layout outright. Enhanced CDs may therefore not match a source and are reported as unidentified
  rather than partially guessed.
- **Raw device permissions (macOS).** Reading the TOC of an unmounted disc opens `/dev/rdiskN`, which may
  require the operator group; a refused read is reported as unidentified.
- **No track entries on device scans.** A Unix audio CD has no filesystem, so a device-root scan has no
  `.cda` entries to attach track records to; the disc record still carries the identificators, track count
  and per-track lengths.
