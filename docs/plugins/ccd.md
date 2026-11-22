# CloneCD (CCD / IMG / SUB) disk image indexing plugin

Implements #726 and #868 on the metadata indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.Metadata.Ccd/` → plugin id `skycd.ccd`.
Tests: `tests/SkyCD.Plugin.Metadata.Ccd.Tests/`.
Entry point: the `.ccd` descriptor; the plugin claims `.ccd` files and resolves the IMG companion.

## What it provides

One metadata capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Index work items | `CcdImageIndexPlugin : IIndexWorkItemProvider` |

A CloneCD image is a set of companion files that share a base name:

| File | Contents |
|---|---|
| `name.ccd` | Text (INI) descriptor with the disc sessions and track table. This is the entry point the plugin claims. |
| `name.img` | Raw track data; every track is stored back to back. |
| `name.sub` | Optional subchannel data. It is not needed to read the filesystem and is never opened. |

The descriptor is indexed as the image. The IMG file is not claimed on its own, because a
bare `.img` commonly holds a floppy or hard-disk image that does not follow the CCD layout.

## Reading the image

- The descriptor is parsed as INI text. Only `[Disc]` (`ImageFile`, `DataTracksScrambled`) and
  `[TRACK n]` (`MODE`, `INDEX 1`) keys are used; everything else is ignored.
- The IMG companion is resolved next to the descriptor. A descriptor `ImageFile` entry wins over the
  base name, and the lookup is case-insensitive so sets copied from case-insensitive filesystems work.
  Only the simple file name of `ImageFile` is honored, so a damaged or hostile descriptor cannot point
  outside the descriptor's directory.
- Supported track modes are `MODE1/2048`, `MODE1/2352`, `MODE2/2048`, `MODE2/2336`, and `MODE2/2352`.
  The plugin maps the 2048-byte user-data area of every raw sector into a plain 2048-byte-per-sector view
  for the ISO 9660 reader.
- The first data track is indexed. Audio tracks before it are skipped by honoring their declared sector
  index, and the data track ends where the next track begins or where the IMG file ends. A single data
  track with any number of audio tracks after it is therefore read correctly.
- The filesystem is ISO 9660 with Joliet names, read through DiscUtils.

## Indexing

When metadata-collecting plugins are enabled, every contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time from the IMG file into a `DeleteOnClose` temporary file
and deletes it as soon as the host disposes the stream, including when extraction fails or is
cancelled. Entry, per-file, and total extraction limits bound what is materialized.

The IMG file is opened read-only and shared for reading, so indexing does not lock a set that another
tool is copying or mounting.

## Deliberately unsupported

Rejected with a clear message instead of being misread:

- Images whose descriptor sets `DataTracksScrambled=1`; their data track is scrambled and not an ISO
  filesystem.
- Audio-only images and descriptors without a supported data track.
- Unknown track modes.
- Sessions after the first data track are not browsed.

A missing IMG companion, damaged descriptor, or unreadable filesystem contributes no entries instead of
failing the scan.

## Dependencies and Licensing

ISO 9660 parsing reuses `DiscUtils.Iso9660` `0.16.13` (MPL-2.0), which is already used by
`skycd-iso`, `skycd-nrg`, and `skycd-daa` and is allowed by
`docs/legal/dependency-license-policy.json`. Descriptor parsing and companion resolution add no new
dependency.

## Tests

`tests/SkyCD.Plugin.Metadata.Ccd.Tests/` builds ISO 9660 images with DiscUtils and lays them out as 2048, 2336,
and 2352-byte sectors, then exercises:

- Every supported track mode, data tracks after an audio track, and `ImageFile` overrides.
- Missing companions and traversal attempts in `ImageFile`.
- Damaged descriptors, audio-only sets, and scrambled images.
- One-file-at-a-time extraction, temporary-file cleanup, entry limits, total budget, and cancellation.
- Host indexing through `IndexWorkItemSequence` with a metadata provider.

`Fixtures/Ccd/catalog-sample.ccd` and `catalog-sample.img` remain as a sample set for manual checks.
