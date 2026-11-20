# LHA Archive Index Plugin

## Overview
The `skycd-lha` plugin indexes LHA archives (`*.lha`, `*.lzh`) as read-only
catalogs. During scanning it lists every stored folder and file, including
nested paths, and contributes them to the catalog hierarchy. When at least one
metadata-collecting plugin is enabled it also temporarily extracts each entry so
those plugins can inspect it, then removes the extracted files. It never writes
or modifies the archive.

The plugin is discovered through assembly metadata and capability types
(`IFileFormatPluginCapability` and `IIndexWorkItemProvider`); no `plugin.json`
descriptor is used. A single plugin instance provides both capabilities.

## Supported Archives
- **Container**: LHA header levels 0, 1, and 2 with `-lh0-` (stored) and
  `-lh1-`, `-lh4-` through `-lh7-` compression. Level 0 and level 2 archives
  written by common tools are covered by the fixture and tests.
- **Extensions**: `.lha` and the common Amiga/Japanese variant `.lzh`.
- **Not supported**: entries compressed with methods without a decoder
  (`-lh2-`, `-lh3-`) and damaged or truncated data. Listing still works for
  those entries because only headers are parsed; extraction skips them and
  records a typed failure (`LHA_UNSUPPORTED`, `LHA_DAMAGED`). A file whose first
  header does not look like an LHA member is rejected as `LHA_DAMAGED` instead
  of being reported as an empty archive.

## Metadata Collection
When metadata-collecting plugins are enabled, the plugin contributes one work
item per contained file. Extraction is bounded by `LhaIndexLimits`
(100,000 entries, 256 MiB per entry, 1 GiB total by default); entries beyond the
budget are skipped. All extracted files live under a unique
`skycd-lha-index-<guid>` temporary directory created for a single indexing pass.
That directory is deleted when the pass finishes, including on provider failure
or cancellation. Non-seekable sources are buffered to a temporary file inside
the same directory first.

Symbolic-link entries and directory entries are not extracted as work items.
The library verifies each entry's CRC-16 while it is read, so entries whose data
does not match the header are skipped rather than indexed.

## Resource and Security Bounds
- Entry paths are normalized and unsafe names (absolute paths, drive-qualified
  paths, `..` segments) are skipped before extraction; every resolved path is
  re-checked to stay under the temporary root, so archive contents never escape
  it.
- Entry and total-size budgets stop a hostile archive from exhausting disk space.
- Damaged headers, unsupported methods, and CRC mismatches are reported through
  failure telemetry or skipped work items rather than crashing indexing.
- Reads are read-only; attempts to use the format for saving are blocked by the
  host because `CanWrite` is `false`.

## Dependencies and Licensing
The plugin adds `Hst.Compression` `1.0.104` (MIT) and its transitive dependency
`Hst.Core` `1.0.104` (MIT). Both expose `PackageLicenseExpression` `MIT`, target
`net10.0`, and are allowed by `docs/legal/dependency-license-policy.json`
(OSADL classifies MIT as compatible with SkyCD's BSD-2-Clause license). The
package is actively maintained and replaces a hand-written LHA decoder, which
would need to cover several compression methods and header levels.
