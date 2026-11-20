# BIN / CUE disk image indexing plugin

Implements #723 on the file-format and indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.BinCue/` → plugin id `skycd.bincue`.
Tests: `tests/SkyCD.Plugin.BinCue.Tests/`.
Format: `skycd-bincue`, extension `.cue`, MIME types `application/x-cue` and `application/x-bincue`,
read-only.

## What it provides

One read-only capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Catalog hierarchy | `BinCueImageIndexPlugin : IFileFormatPluginCapability` |
| Index work items | `BinCueImageIndexPlugin : IIndexWorkItemProvider` |

A BIN/CUE image is a pair of companion files that share a base name:

| File | Contents |
|---|---|
| `name.cue` | Small text sheet that declares the FILE, its tracks, and their start times. This is the entry point the plugin claims. |
| `name.bin` | Raw track data; tracks are stored back to back at fixed sector sizes. |

The sheet is indexed as the image. A bare `.bin` is not claimed as a format on its own, because a bare
`.bin` is also used for firmware and disk images that do not follow the CUE layout.

## Reading the image

- The sheet is parsed as text (UTF-8 with a Latin-1 fallback, BOM stripped). Only `FILE`, `TRACK`, and
  `INDEX` statements are used; `REM`, `TITLE`, `PERFORMER`, `PREGAP`, and the like are ignored.
- The BIN companion is resolved next to the sheet. The sheet's `FILE` name wins over the base name, and
  the lookup is case-insensitive so sets copied from case-insensitive filesystems work. Only the simple
  file name of `FILE` is honored, so a damaged or hostile sheet cannot point outside the sheet's
  directory.
- Supported track modes are `MODE1/2048`, `MODE1/2352`, `MODE2/2048`, `MODE2/2336`, `MODE2/2352`,
  `CDI/2336`, and `CDI/2352`. The plugin maps the 2048-byte user-data area of every raw sector into a
  plain 2048-byte-per-sector view for the ISO 9660 reader.
- `INDEX 01` gives the start of a track's payload; `INDEX 00` (the pregap) is ignored. Times are read as
  `mm:ss:ff` with 75 frames per second.
- The first data track is indexed. Audio tracks before it are skipped by honoring their declared sector
  index, and the data track ends where the next track begins or where the BIN file ends.
- The filesystem is ISO 9660 with Joliet names, read through DiscUtils.

## Indexing

When metadata-collecting plugins are enabled, every contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time from the BIN file into a `DeleteOnClose` temporary file
and deletes it as soon as the host disposes the stream, including when extraction fails or is
cancelled. Entry, per-file, and total extraction limits bound what is materialized. Nothing is ever
written outside the system temporary directory.

The BIN file is opened read-only and shared for reading, so indexing does not lock a set that another
tool is copying or mounting.

## Deliberately unsupported

Rejected with a clear message instead of being misread:

- Audio-only sheets and sheets without a data track.
- Data tracks stored in a non-binary container such as `FILE ... WAVE`.
- Track modes outside the list above.
- Sheets with no `INDEX 01` on the data track, or whose data track starts past the end of the BIN file.
- Multi-file sets where the data track lives in a second FILE are not browsed; only the first data
  track of a single-file set is indexed.

A missing BIN companion, damaged sheet, or unreadable filesystem contributes no entries instead of
failing the scan.

## Dependencies and Licensing

ISO 9660 parsing reuses `DiscUtils.Iso9660` `0.16.13` (MPL-2.0), which is already used by
`skycd-iso`, `skycd-nrg`, `skycd-daa`, and `skycd-ccd` and is allowed by
`docs/legal/dependency-license-policy.json`. Sheet parsing and companion resolution add no new
dependency.

## Tests

`tests/SkyCD.Plugin.BinCue.Tests/` builds ISO 9660 images with DiscUtils and lays them out as 2048,
2336, and 2352-byte sectors, then exercises:

- Every supported track mode, data tracks after an audio track, and multi-track boundaries.
- Quoted `FILE` names with spaces, case-insensitive companion lookup, and traversal attempts.
- Damaged sheets, binary sheets, audio-only sheets, missing `INDEX 01`, and tracks past the end of the
  file, and non-binary `FILE` containers.
- `INDEX 00` pregap handling and invalid time stamps.
- One-file-at-a-time extraction, temporary-file cleanup, entry limits, total budget, and cancellation.
- Host indexing through `IndexWorkItemSequence` with a metadata provider.

`Fixtures/BinCue/catalog-sample.cue` and `catalog-sample.bin` also feed the shared
`FileFormatCompatibilityTests`.
