# Audio CD metadata collection plugin

Implements #732 on the volume-level media contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.AudioCD/` → plugin id `SkyCD.Plugin.Metadata.AudioCD`.
Tests: `tests/SkyCD.Plugin.Metadata.AudioCD.Tests/`.

An audio CD has no filesystem to read tags from, so identification and enrichment exist only at the volume
level. The plugin contributes one `IAudioCdMetadataCapability` (disc and track records plus cover art) and one
`IPluginConfigurationCapability` (the embedded Options → Metadata → Audio CD page). Discovery uses assembly metadata
and implemented capability types only — there is no `plugin.json`.

## Identification

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

## Metadata schema

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

## Sources and network behaviour

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

## Cover art

The front cover is retrieved once at the larger of the two configured sizes and rendered twice, so disc and
track covers can differ in size without a second download. Size is bounded to 32–2048 px, default 250 px
(the issue's reference tile). Images are never upscaled: an image already within the requested edge is
passed through byte-for-byte with its original content type, and art bytes are withheld rather than
published broken when decoding fails.

## Settings

Enable the plugin to show its embedded **Options → Metadata → Audio CD** page. Navigation keeps the draft;
Options Save persists and applies it, while Cancel/close leaves stored settings unchanged.
See [SDK configuration contracts](sdk-contracts.md#embedded-options-configuration-v3-migration-828).

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

## Host integration

The plugin never writes the catalog. Records are staged into the media cache batch the scanning view model
already manages, and published atomically with the file records:

- `IAudioCdMetadataCapability` / `AudioCdDiscRequest` / `AudioCdCapture` — the plugin seam in
  `SkyCD.Plugin.Abstractions/Capabilities/Media/AudioCdContracts.cs`;
- `AudioCdExtensionManager` (`src/SkyCD.Plugin.Host/AudioCd/`) — generic host manager: priority order,
  per-plugin settings resolution, failure isolation through `PluginFailure.Publish`, cancellation passthrough,
  registered as `IAudioCdMetadataProvider`;
- `AudioCdMetadataService` (`src/SkyCD.Presentation/MediaCache/`) — platform-neutral disc-root discovery,
  artifact staging (`preview:0`), and per-entry fingerprints, mirroring the file metadata service;
- `SystemMediaDriveProvider` / `MediaCatalogScanner` — on Unix an audio CD exists only as a device node;
  sysfs and IOKit probes offer it for scanning, and the scanner catalogues it as a media root without
  traversing a filesystem;
- `DiscoveredPluginFactory` gains an `IAudioCdMetadataCapability.PrimaryExtension` fallback so the plugin has
  an icon identity (`cda`) without a file-format capability;
- `MainWindowViewModel.StageMetadataAsync` runs the disc step before the file loop and reports failures as a
  scan warning.

## Dependencies

`dotnet list Plugins/SkyCD.Plugin.Metadata.AudioCD/SkyCD.Plugin.Metadata.AudioCD.csproj package --include-transitive`:

| Package | Version | License | Use |
|---|---|---|---|
| SkiaSharp | 3.119.4 | MIT | Decode and downscale cover art |
| SkiaSharp.NativeAssets.Win32 / Linux / macOS | 3.119.4 | MIT | Native renderer for each RID |
| Avalonia, Avalonia.Desktop | 12.1.6 | MIT | Embedded settings page, same pattern as Sample.Configuration |

All three families already pass the Trivy license gate in `docs/legal/dependency-policy.md` and are already
used by the repo (`SkyCD.Plugin.Metadata.Image` uses SkiaSharp; every UI plugin uses Avalonia), so no policy
change is needed. Network and JSON
use the MIT .NET runtime (`HttpClient`, `System.Text.Json`); no HTTP or JSON package is added.

Alternatives considered:

- **MetaBrainz.MusicBrainz.Client**: the maintained client for WS/2, but LGPL-3.0 — a blocked license family.
  The required lookup is one GET plus field extraction, so the BCL client replaces it.
- **TagLib#**: LGPL-2.1; also has nothing to read on an audio CD, which carries no tag blocks.
- **freedb libraries**: unmaintained; GnuDb's protocol is a single HTTP GET with a plain-text body.

## Testing

`tests/SkyCD.Plugin.Metadata.AudioCD.Tests/` uses no network and no binary blobs:

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
- **No fuzzy matching or manual disc-id entry.** Only exact disc-id lookups are performed; a pressed disc
  whose TOC differs by a frame will not match.
- **GnuDb multi-match** (`211`) uses the first entry offered; no scoring across candidates.
- **`.cda` files are virtual**: they carry no bytes, so track records exist only when the drive was scanned
  with them present.
- **Settings at extraction time** are resolved through the host's existing `PluginConfigurationStore`. PR #768
  (eComic) generalises settings consumption (`IPluginSettingsConsumer`) and adds a settings digest to provider
  identity; if it lands first, this manager's resolver delegate should move to that contract.

## References

- [MusicBrainz Disc ID Calculation](https://musicbrainz.org/doc/Disc_ID_Calculation)
- [MusicBrainz WS/2 discid lookup](https://musicbrainz.org/doc/MusicBrainz_API)
- [GnuDb cddb protocol](https://gnudb.org/cddb.php)
- [Cover Art Archive API](https://musicbrainz.org/doc/Cover_Art_Archive/API)
- [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- [SkiaSharp](https://www.nuget.org/packages/SkiaSharp)
- [Avalonia](https://www.nuget.org/packages/Avalonia)
