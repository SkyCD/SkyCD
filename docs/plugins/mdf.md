# Alcohol 120% (MDS / MDF) disk image indexing plugin

Implements #724 and #868 on the metadata indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.Metadata.Mdf/` → plugin id `skycd.mdf`.
Tests: `tests/SkyCD.Plugin.Metadata.Mdf.Tests/`.
Entry point: the `.mds` descriptor; the plugin also accepts an MDF path directly.

## What it provides

One metadata capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Index work items | `MdfImageIndexPlugin : IIndexWorkItemProvider` |

An Alcohol image is a pair of companion files that share a base name:

| File | Contents |
|---|---|
| `name.mds` | Binary descriptor with the media type, sessions, and track table. This is the entry point the plugin claims. |
| `name.mdf` | Raw track data with 2048, 2336, or 2352-byte sectors. |

The MDF file is not claimed on its own, because it carries no descriptor and another
product may use the same extension. The plugin still accepts an MDF path directly, so images whose
descriptor was lost remain browseable.

## Reading the image

- The descriptor's fixed header is validated: the `MEDIA DESCRIPTOR` signature, a version of `1.10`,
  `1.20`, or `2.00`, a known CD/DVD/Blu-ray media type, and a plausible session count. The rest of
  the descriptor is not needed to browse the filesystem.
- The MDF companion is resolved next to the opened file by base name. The lookup is case-insensitive
  so sets copied from case-insensitive filesystems work, and it only ever looks inside the opened
  file's directory.
- The data layout is not trusted from the descriptor alone: the plugin probes the image for an
  ISO 9660 primary volume descriptor at sector 16 in each supported layout (`2048/0`, `2352/16`,
  `2352/24`, `2336/8`) and maps the 2048-byte user-data area of every raw sector into a plain
  2048-byte-per-sector view for the ISO 9660 reader.
- The filesystem is ISO 9660 with Joliet names, read through DiscUtils.

## Indexing

When metadata-collecting plugins are enabled, every contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time from the MDF file into a `DeleteOnClose` temporary file
and deletes it as soon as the host disposes the stream, including when extraction fails or is
cancelled. Entry, per-file, and total extraction limits bound what is materialized.

The MDF file is opened read-only and shared for reading, so indexing does not lock a set that another
tool is copying or mounting.

## Deliberately unsupported

Rejected with a clear message instead of being misread:

- Descriptors with an unknown signature, unsupported version, unknown media type, or invalid session
  count.
- Images whose sector layout holds no ISO 9660 volume descriptor, such as raw audio-only images.

A missing MDF companion, damaged descriptor, or unreadable filesystem contributes no entries instead of
failing the scan. Only the first data track is browsed; sessions after it are not.

## Dependencies and Licensing

ISO 9660 parsing reuses `DiscUtils.Iso9660` `0.16.13` (MPL-2.0), which is already used by
`skycd-iso`, `skycd-nrg`, `skycd-daa`, and `skycd-ccd` and is allowed by
`docs/legal/dependency-license-policy.json`. Descriptor parsing and companion resolution add no new
dependency.

## Tests

`tests/SkyCD.Plugin.Metadata.Mdf.Tests/` builds ISO 9660 images with DiscUtils and lays them out as 2048, 2336,
and 2352-byte sectors, then exercises:

- Every supported sector layout, images opened from the MDF file, and MDF files without a descriptor.
- Missing companions, damaged descriptors, unsupported versions and media types, and non-ISO payloads.
- One-file-at-a-time extraction, temporary-file cleanup, entry limits, total budget, and cancellation.
- Host indexing through `IndexWorkItemSequence` with a metadata provider.

`Fixtures/Mdf/catalog-sample.mds` and `catalog-sample.mdf` remain as a sample set for manual checks.
