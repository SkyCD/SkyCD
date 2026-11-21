# ARC Archive Index Plugin

## Overview
The `skycd-arc` plugin indexes SEA ARC archives (`*.arc`) as read-only
catalogs. During scanning it lists every stored folder and file, including
nested paths, and contributes them to the catalog hierarchy. When at least one
metadata-collecting plugin is enabled it also temporarily extracts each entry so
those plugins can inspect it, then removes the extracted files. It never writes
or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: SEA ARC with the short (16-bit) and extended (32-bit) header
  layouts, including the extended date/time and `=TX=` text fields.
- **Stored methods**: uncompressed entries (`0`, `4`) and squashed entries
  (`7`). Entries compressed with crunched or packed methods (`1`–`3`, `5`,
  `6`) are listed but not extracted and are reported as `ARC_UNSUPPORTED`.
- **Not supported**: damaged or truncated archives. Headers parsed before the
  damage are still listed and the damage is reported as `ARC_DAMAGED`; a file
  whose first header does not look like an ARC member is rejected rather than
  reported as an empty archive.
- A zero-length file or a file that starts with the end-of-archive marker
  (`0x1A`) is treated as an empty archive, which is how some tools "delete" an
  ARC component.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Extraction is bounded by `ArcIndexLimits`
(100,000 entries, 256 MiB per entry, 1 GiB total by default); entries beyond
the budget are skipped. All extracted files live under a unique
`skycd-arc-index-<guid>` temporary directory created for a single indexing
pass. That directory is deleted when the pass finishes, including on provider
failure, cancellation, or when the entry budget is exceeded. Non-seekable
sources are buffered to a temporary file inside the same directory first.

Directory entries and entries with unsupported methods are not extracted as
work items. Duplicate entry names keep the first entry so a hostile archive
cannot overwrite already extracted data.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, drive-qualified
  paths, `..` segments) are skipped before extraction; every resolved path is
  re-checked to stay under the temporary root, so archive contents never escape
  it.
- Entry and total-size budgets stop a hostile archive from exhausting disk
  space; exceeding the entry-count bound aborts the pass without leaving files
  behind.
- Header damage and unsupported methods are reported through typed
  diagnostics (`ARC_DAMAGED`, `ARC_UNSUPPORTED`) on the listing rather than
  crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin has no external package dependencies: the ARC header parser and the
stored/squashed decoder are implemented in the plugin itself. That keeps the
dependency tree unchanged and avoids pulling in a general-purpose archive
library for a small, stable format.
