# ARJ Archive Index Plugin

## Overview
The `skycd-arj` plugin indexes ARJ archives (`*.arj`) as read-only catalogs.
During scanning it lists every stored folder and file, including nested paths,
and contributes them to the catalog hierarchy. When at least one
metadata-collecting plugin is enabled it also temporarily extracts each entry so
those plugins can inspect it, then removes the extracted files. It never writes
or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: ARJ headers with the `0xEA60` marker, matching archive and
  local headers with CRC-checked header blocks.
- **Compression methods**: stored (method 0), and methods 1-4 through
  SharpCompress. Methods 1-3 are only decompressible when the original size is
  at most 128 KB, a limit of the ARJ algorithm itself; larger entries raise an
  unsupported-compression error at extraction time while still being listed.
- **Not supported**: encrypted archives, multi-volume sets that require a
  companion volume, and unknown compression methods. These are reported with
  typed diagnostics (`ARJ_ENCRYPTED`, `ARJ_MULTIVOLUME`, `ARJ_DAMAGED`) instead
  of being partially indexed. Listing still works for entries behind an
  unsupported compression method because only headers are parsed.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Extraction is bounded by `ArjIndexLimits`
(100,000 entries, 256 MiB per entry, 1 GiB total by default); entries beyond the
budget are skipped. All extracted files live under a unique
`skycd-arj-index-<guid>` temporary directory created for a single indexing pass.
That directory is deleted when the pass finishes, including on provider failure
or cancellation. Non-seekable sources are buffered to a temporary file inside the
same directory first, because the ARJ reader is forward-only.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, `..` segments)
  are skipped before extraction; every resolved path is re-checked to stay under
  the temporary root, so archive contents never escape it.
- Entry and total-size budgets stop a hostile archive from exhausting disk space.
- Damaged headers raise a damaged-archive error and are reported through plugin
  failure telemetry rather than crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds no new NuGet dependency: ARJ parsing reuses `SharpCompress`
`0.50.8` (MIT), which is already used by `skycd-7z` and `skycd-ecomic` and is
allowed by `docs/legal/dependency-license-policy.json`. `SharpCompress` targets
`net10.0` and remains actively maintained.
