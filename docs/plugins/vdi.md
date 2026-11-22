# VirtualBox VDI disk image indexing plugin

Implements #720 on the file-format and indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.FileFormat.Vdi/` → plugin id `skycd.vdi`.
Tests: `tests/SkyCD.Plugin.FileFormat.Vdi.Tests/`.

## What it provides

One read-only capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Catalog hierarchy | `VdiImageIndexPlugin : IFileFormatPluginCapability` |
| Index work items | `VdiImageIndexPlugin : IIndexWorkItemProvider` |

The plugin claims the `.vdi` extension with MIME types `application/x-virtualbox-vdi` and
`application/x-vdi`.

## Reading the image

The VirtualBox container is parsed in managed code, so indexing works on Windows, macOS, and Linux
without VirtualBox or any other native dependency.

- The 64-byte pre-header text, the image header magic, the header version, the image type (fixed or
  dynamic), and the disk geometry are validated before any guest byte is read. Only the VirtualBox 1.2
  header layout (0x0001_0002) is accepted; older or unknown versions, unsupported sector sizes, and
  encrypted images are reported as unsupported instead of misread.
- Fixed images map guest offsets to a constant file offset. Dynamic images resolve each sector through
  the block map, which is loaded a sector at a time and kept in a small eviction cache, so a damaged
  block entry only affects the reads that touch it. Unallocated blocks read back as zeros.
- Snapshot and differencing layers need their base image, which this plugin does not resolve; such
  images are reported as missing their parent.
- Guest FAT, NTFS, and ext2/3/4 volumes are opened through DiscUtils. A disk without a partition table
  is treated as one volume, and several volumes are prefixed with their volume label.
- Unsafe paths, reparse points, device entries, damaged entries, and overly deep trees are skipped
  instead of failing the whole image.

## Indexing

When metadata-collecting plugins are enabled, each contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time into a `DeleteOnClose` temporary file, serializes
concurrent extractions, and deletes the stream when the host disposes it. The temporary directory is
removed when the item set is disposed, including on failure or cancellation. Entry, per-file, and
total extraction limits bound what is materialized.

## Deliberately unsupported

Encrypted images, snapshot chains, and files inside guest file systems that this plugin cannot open are
rejected or skipped with a clear result rather than guessed. The plugin never shells out to
`VBoxManage`, `qemu-img`, or similar tools.

## Tests

`tests/SkyCD.Plugin.FileFormat.Vdi.Tests/` builds small fixed and dynamic VDI images in memory around a FAT volume,
then exercises catalog listing, one-file-at-a-time extraction, cleanup, limits, cancellation,
stream-based opening, and the damaged, missing parent, unsupported version and sector size, and
non-image rejection paths. `Fixtures/Vdi/catalog.vdi` also feeds the shared
`FileFormatCompatibilityTests`.
