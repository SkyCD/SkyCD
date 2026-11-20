# QCOW2 disk image indexing plugin

Implements #721 on the file-format and indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.Qcow2/` → plugin id `skycd.qcow2`.
Tests: `tests/SkyCD.Plugin.Qcow2.Tests/`.

## What it provides

One read-only capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Catalog hierarchy | `Qcow2ImageIndexPlugin : IFileFormatPluginCapability` |
| Index work items | `Qcow2ImageIndexPlugin : IIndexWorkItemProvider` |

The plugin claims extension `.qcow2` with MIME types `application/x-qemu-disk` and
`application/x-qcow2`.

## Reading the image

The container is parsed in managed code, so indexing works on Windows, macOS, and Linux without
`qemu-img` or any other native dependency.

- Version 2 and version 3 headers are supported.
- Guest clusters are mapped through the L1/L2 tables. Unallocated clusters read from the backing
  file, or as zeros when there is no backing file. Zero clusters are honored.
- Deflate-compressed clusters are decompressed lazily. Both raw deflate and zlib-wrapped payloads are
  accepted, because different tools have written both.
- Backing files are resolved relative to the image that names them and must stay in that directory.
  The chain is limited to eight layers and cycles are rejected. Images opened from a stream without a
  local path cannot resolve backing files and are reported as missing one.
- Guest FAT, NTFS, and ext2/3/4 volumes are opened through DiscUtils. A disk without a partition table
  is treated as one volume, and several volumes are prefixed with their volume label.
- Unsafe paths (`.`/`..`, control characters), reparse points, device entries, damaged entries, and
  overly deep trees are skipped instead of failing the whole image.

## Indexing

When metadata-collecting plugins are enabled, each contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time into a `DeleteOnClose` temporary file, serializes
concurrent extractions, and deletes the stream when the host disposes it. The temporary directory is
removed when the item set is disposed, including on failure or cancellation. Entry, per-file, and
total extraction limits bound what is materialized.

## Deliberately unsupported

These are rejected with a clear message rather than misread:

- Encrypted images (AES or LUKS).
- zstd-compressed clusters.
- Extended L2 entries.
- Raw external data files.

Unknown incompatible feature bits are also rejected, as the format requires.

## Tests

`tests/SkyCD.Plugin.Qcow2.Tests/` builds images in memory (plain, deflate-compressed, and overlay
images with a backing file) around a FAT volume that DiscUtils creates, then exercises catalog
listing, one-file-at-a-time extraction, cleanup, limits, cancellation, and the damaged, encrypted,
and unsupported rejection paths. `Fixtures/catalog-sample.qcow2` also feeds the shared
`FileFormatCompatibilityTests`.
