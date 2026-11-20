# RAR Archive Index Plugin

## Overview
The `skycd-rar` plugin indexes RAR (`*.rar`) archives as read-only catalogs. It
lists every stored folder and file, including nested paths, and contributes them
to the catalog hierarchy during scanning. When at least one metadata-collecting
plugin is enabled it also temporarily extracts each entry so those plugins can
inspect it, then removes the extracted files. It never writes or modifies the
archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Containers**: RAR4 (`Rar!\x1A\x07\x00`) and RAR5 (`Rar!\x1A\x07\x01\x00`)
  archives. Files that do not carry either signature are rejected with
  `RAR_DAMAGED` instead of being reported as an empty archive.
- **Compression methods**: every method the reader supports, including stored
  entries and the RAR/RAR5 compressed methods.
- **Not supported**: encrypted entries or encrypted headers, multi-volume sets
  that need a companion volume, and entries whose data continues in a missing
  volume. These are reported with typed diagnostics (`RAR_ENCRYPTED`,
  `RAR_MULTIVOLUME`) rather than being partially indexed.

Multi-volume detection is deliberately defensive: the plugin checks the main
archive header volume flag, the end header "next volume" flag, per-entry
split-after flags, and whether a companion volume file (`set.part2.rar`,
`set.r00`, ...) exists next to the source. A source whose data ends inside an
entry that continues elsewhere raises `RAR_MULTIVOLUME` instead of silently
listing truncated data as a complete file.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Extraction is bounded by `RarIndexLimits` (100,000
entries, 256 MiB per entry, 1 GiB total by default); entries beyond the budget
are skipped. All extracted files live under a unique `skycd-rar-index-<guid>`
temporary directory created for a single indexing pass. That directory is
deleted when the pass finishes, including on provider failure or cancellation.
Non-seekable sources are buffered to a temporary file inside the same directory
first.

RAR archives written by archivers that store Quick Open service headers repeat
every file name as a zero-length directory-like header. Those headers are
skipped so each stored file is listed and extracted exactly once.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, drive-qualified
  paths, `..` segments) are skipped before extraction; every resolved path is
  re-checked to stay under the temporary root, so archive contents never escape
  it.
- Entry and total-size budgets stop a hostile archive from exhausting disk space.
- The output of a decompression stream is bounded while it is copied, so a
  header that understates the uncompressed size cannot fill the disk.
- Damaged headers raise a damaged-archive error and are reported through plugin
  failure telemetry rather than crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds no new NuGet dependency: RAR header parsing and decompression
reuse `SharpCompress` `0.50.8` (MIT), which is already used by `skycd-7z`,
`skycd-arj`, and `skycd-ecomic` and is allowed by
`docs/legal/dependency-license-policy.json`. `SharpCompress` targets `net10.0`
and remains actively maintained.
