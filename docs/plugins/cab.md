# CAB Archive Index Plugin

## Overview
The `skycd-cab` plugin indexes Microsoft Cabinet archives (`*.cab`) as
read-only catalogs. During scanning it lists every stored folder and file,
including nested paths, and contributes them to the catalog hierarchy. When at
least one metadata-collecting plugin is enabled it also temporarily extracts
each entry so those plugins can inspect it, then removes the extracted files.
It never writes or modifies the cabinet.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: cabinets with the `MSCF` signature, version 1.1 through 1.3,
  including the version 1.1 two-byte attribute prefix in file names and UTF-8
  names announced by the header flags.
- **Compression**: stored (type 0) and MSZIP (type 1) folders. MSZIP blocks are
  inflated one CFDATA block at a time; blocks that reference the previous
  block's window are reported as damaged and are not extracted.
- **Not supported**: folded or LZX folders (types 2 and 3) are listed but not
  extracted, and damaged or truncated headers fail with `CAB_DAMAGED`.
  Multi-volume cabinets (a declared previous or next cabinet) are not inspected
  because only the first cabinet of the set is available; the work-item pass
  returns an empty set rather than a partial listing.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Extraction is bounded by `CabIndexLimits`
(100,000 entries, 256 MiB per entry, 1 GiB total by default); entries beyond
the budget are skipped. All extracted files live under a unique
`skycd-cab-index-<guid>` temporary directory created for a single indexing
pass. That directory is deleted when the pass finishes, including on provider
failure or cancellation. Non-seekable sources are buffered to a temporary file
inside the same directory first.

Duplicate entry names keep the first entry so a hostile archive cannot
overwrite already extracted data.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, drive-qualified
  paths, `..` segments) are skipped before extraction; every resolved path is
  re-checked to stay under the temporary root, so cabinet contents never escape
  it.
- Entry and total-size budgets stop a hostile archive from exhausting disk
  space; skipped members are advanced over without being written.
- Header damage, unsupported compression, and failed decoding are reported
  through typed failures or skipped work items rather than crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin has no external package dependencies: cabinet header parsing and the
stored/MSZIP folder decoder are implemented in the plugin itself, following the
published Microsoft Cabinet format.
