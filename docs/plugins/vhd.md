# VHD / VHDX disk image indexing plugin

Implements #718 and #868 on the metadata indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.Metadata.Vhd/` → plugin id `skycd.vhd`.
Tests: `tests/SkyCD.Plugin.Metadata.Vhd.Tests/`.

## What it provides

One metadata capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Index work items | `VhdImageIndexPlugin : IIndexWorkItemProvider` |

The plugin claims extensions `.vhd` and `.vhdx`.

## Reading the image

Images are parsed in managed code, so indexing works on Windows, macOS, and Linux without Hyper-V
or any other native dependency. The container format is detected from its signature, so a fixed VHD
whose header appears at both the start and end of the file is recognized as well as a dynamic one.

- VHD and VHDX dynamic, fixed, and differencing images are supported. Differencing parents are
  resolved relative to the image that names them and must stay in that directory; the whole chain is
  loaded into DiscUtils, limited to eight layers with cycle detection.
- Guest FAT, NTFS, and ext2/3/4 volumes are opened through DiscUtils. A disk without a partition table
  is treated as one volume, and several volumes are prefixed with their volume label.
- Unsafe paths (`.`/`..`, control characters), reparse points, device entries, damaged entries, and
  overly deep trees are skipped instead of failing the whole image.
- Images opened from a stream without a local path cannot resolve parents and are reported as
  missing one. A non-seekable stream is copied to a temporary file that is removed on dispose.

## Indexing

When metadata-collecting plugins are enabled, each contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time into a `DeleteOnClose` temporary file, serializes
concurrent extractions, and deletes the stream when the host disposes it. The temporary directory is
removed when the item set is disposed, including on failure or cancellation. Entry, per-file, and
total extraction limits bound what is materialized.

## Deliberately unsupported

Unknown or damaged containers, encrypted content inside the guest file system, and parents outside the
image directory are rejected with a clear message rather than misread. The plugin never shells out to
`diskpart`, `qemu-img`, or similar tools.

## Tests

`tests/SkyCD.Plugin.Metadata.Vhd.Tests/` builds dynamic VHD and VHDX images in memory around a FAT volume that
DiscUtils creates, then exercises catalog listing, one-file-at-a-time extraction, cleanup, limits,
cancellation, stream-based opening, VHD differencing parent resolution, and the damaged, missing
parent, parent-outside-directory, and non-image rejection paths. `Fixtures/catalog-sample.vhd` remains as a sample set for manual checks.
